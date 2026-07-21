using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

public class TypeFinder(SemanticModel sem, PreferredNamespaceList namespaces) : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.ValueText != "var" && node.Identifier.ValueText != "dynamic") {
            return RewriteName(node) ?? node;
        }

        return node;
    }

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        => RewriteName(node) ?? base.VisitQualifiedName(node);

    SyntaxNode RewriteName(NameSyntax node) => Rewrite(node, node => {
        var info = sem.GetSymbolInfo(node);

        if (node is IdentifierNameSyntax && !IsQualifierTarget(node)
            && TryQualifyEnumLiteral(node, info) is { } qualifiedEnum) {
            return qualifiedEnum;
        }

        if (node.IsTypeUsage()) {
            if (info.Symbol is {}) {
                return node; // Already resolved
            }

            // Symbol is not found or ambiguous. Try to resolve it.

            // If any candidate is a non-type symbol (e.g. a static member, enum value, etc.),
            // qualify it via its containing type to resolve the ambiguity.
            // Prefer the most accessible candidate (Public beats Private, etc.).
            var nonTypeSymbol = info.CandidateSymbols
                .Where(s => s is not ITypeSymbol)
                .OrderByDescending(s => s.DeclaredAccessibility)
                .FirstOrDefault();

            if (nonTypeSymbol?.ContainingType is { } containingType) {
                return QualifiedName(containingType.ToNameSyntax(), IdentifierName(nonTypeSymbol.Name)).WithTriviaFrom(node);
            }

            // Try to find the right symbol
            var typeSymbols = info.CandidateSymbols.OfType<ITypeSymbol>() // Symbols found by Roslyn
                .Concat(RoslynHelpers.FindTypesByName(sem, node.ToString())) // Symbols found by case-insensitive name search
                .ToList();

            if (typeSymbols.Count > 0) {
                var contextual = sem.TryGetContextualType(node); // Prefer contextual type if available
                if (namespaces.PickType(typeSymbols, contextual) is {} chosen) {
                    return chosen.ToNameSyntax().WithTriviaFrom(node);
                }
            }
        }

        return null;
    });

    /// <summary>
    /// True when <paramref name="node"/> is already explicitly qualified/labelled by its parent
    /// (e.g. the right-hand side of a member access or qualified name, or a named-argument/
    /// named-equals label). These positions must never be re-qualified.
    /// </summary>
    static bool IsQualifierTarget(NameSyntax node)
        => node.Parent switch {
            MemberAccessExpressionSyntax m => m.Name == node,
            QualifiedNameSyntax q => q.Right == node,
            MemberBindingExpressionSyntax b => b.Name == node,
            NameColonSyntax nc => nc.Name == node,
            NameEqualsSyntax ne => ne.Name == node,
            _ => false,
        };

    /// <summary>
    /// Rewrites a bare enum literal reference (one that only resolves because of a
    /// <c>using static</c>/<c>global using static</c> directive) into an explicitly
    /// qualified <c>EnumType.Member</c> form, even though it already compiles unqualified.
    /// If the reference is ambiguous across multiple statically-imported enums, the
    /// preferred candidate is chosen using the same rules as <see cref="PreferredNamespaceList"/>.
    /// </summary>
    SyntaxNode TryQualifyEnumLiteral(NameSyntax node, SymbolInfo info)
    {
        static bool IsEnumField(ISymbol s) => s is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum };

        SyntaxNode Qualify(IFieldSymbol field)
        {
            // Sibling member references inside the enum's own body (e.g. `enum Foo { A, B = A + 1 }`)
            // must stay unqualified.
            var enclosingEnum = node.FirstAncestorOrSelf<EnumDeclarationSyntax>();
            if (enclosingEnum is { } && SymbolEqualityComparer.Default.Equals(sem.GetDeclaredSymbol(enclosingEnum), field.ContainingType)) {
                return null;
            }

            return QualifiedName(field.ContainingType.ToNameSyntax(), IdentifierName(field.Name)).WithTriviaFrom(node);
        }

        if (info.Symbol is IFieldSymbol resolved && IsEnumField(resolved)) {
            return Qualify(resolved);
        }

        if (info.Symbol is null) {
            var candidates = info.CandidateSymbols.OfType<IFieldSymbol>().Where(IsEnumField).ToList();

            if (candidates.Count == 1) {
                return Qualify(candidates[0]);
            }

            if (candidates.Count > 1) {
                var contextual = sem.TryGetContextualType(node); // Prefer contextual type if available
                var containingTypes = candidates.Select(f => f.ContainingType);
                if (namespaces.PickType(containingTypes, contextual) is { } chosenType) {
                    var chosenField = candidates.FirstOrDefault(f =>
                        SymbolEqualityComparer.Default.Equals(f.ContainingType, chosenType)) ?? candidates[0];
                    return Qualify(chosenField);
                }
            }
        }

        return null;
    }
}
