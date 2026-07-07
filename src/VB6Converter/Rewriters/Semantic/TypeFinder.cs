using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace VB6Converter.Rewriters.Semantic;

public class TypeFinder(SemanticModel sem, PreferredNamespaceList namespaces) : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => RewriteName(node) ?? base.VisitIdentifierName(node);

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        => RewriteName(node) ?? base.VisitQualifiedName(node);

    SyntaxNode RewriteName(NameSyntax node) => Rewrite(node, node => {
        if (IsTypeUsage(node, sem)) {
            var info = sem.GetSymbolInfo(node);

            if (info.Symbol is ITypeSymbol) {
                return node; // Already resolved
            }
            else {
                // The symbol can be either not found or ambiguous.
            }

            // If any candidate is a non-type symbol (e.g. a static member brought in by
            // 'using static'), qualify it via its containing type to resolve the ambiguity.
            // Prefer the most accessible candidate (Public beats Private, etc.).
            var nonTypeSymbol = info.CandidateSymbols
                .Where(s => s is not ITypeSymbol)
                .OrderByDescending(s => s.DeclaredAccessibility)
                .FirstOrDefault();

            if (nonTypeSymbol?.ContainingType is { } containingType) {
                var memberFqn = $"{containingType.ToDisplayString()}.{nonTypeSymbol.Name}";
                if (memberFqn != node.ToString()) {
                    return SyntaxFactory.ParseName(memberFqn).WithTriviaFrom(node);
                }
                else {
                    return null;
                }
            }

            // Try to find the right symbol
            var typeSymbols = info.CandidateSymbols.OfType<ITypeSymbol>() // Symbols found by Roslyn
                .Concat(RoslynHelpers.FindTypesByName(sem, node.ToString())) // Symbols found by case-insensitive name search
                .Distinct(SymbolEqualityComparer.Default)
                .OfType<ITypeSymbol>()
                .ToList();

            if (typeSymbols.Count > 0) {
                var contextual = sem.TryGetContextualType(node); // Prefer contextual type if available
                var chosen = namespaces.PickType(typeSymbols, contextual); // Resolve using preferred namespaces
                if (chosen != null && !string.Equals(chosen.ToString(), node.ToString(), StringComparison.Ordinal)) {
                    return SyntaxFactory.ParseName(chosen.ToString()).WithTriviaFrom(node);
                }
            }
        }

        return null;
    });

    /// <summary>
    /// Determines whether <paramref name="node"/> sits in a syntactic position where it is
    /// expected to name a type (as opposed to a variable, method, or member name). VB6
    /// identifiers are case-insensitive, so a class/form may be declared with one casing (e.g.
    /// "frmclientesmain") and referenced elsewhere with another (e.g. "frmClientesMain");
    /// this lets those references be corrected to the declared casing.
    /// </summary>
    static bool IsTypeUsage(NameSyntax node, SemanticModel sem)
        => node.Parent switch {
            VariableDeclarationSyntax vds => vds.Type == node,
            ParameterSyntax ps => ps.Type == node,
            ObjectCreationExpressionSyntax oce => oce.Type == node,
            ArrayTypeSyntax at => at.ElementType == node,
            BaseTypeSyntax bts => bts.Type == node,
            CastExpressionSyntax ce => ce.Type == node,
            MethodDeclarationSyntax mds => mds.ReturnType == node,
            PropertyDeclarationSyntax pds => pds.Type == node,
            IndexerDeclarationSyntax ids => ids.Type == node,
            // Bare identifier used as a static-member qualifier (e.g. frmClientesMain.SharedField)
            // that doesn't resolve to anything at all - a strong signal of a mis-cased type name
            // rather than an ordinary unresolved variable/method reference.
            MemberAccessExpressionSyntax maes when maes.Expression == node
                => sem.GetSymbolInfo(node) is { Symbol: null, CandidateSymbols.IsEmpty: true },
            _ => false,
        };
}
