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
        if (node.IsTypeUsage()) {
            var info = sem.GetSymbolInfo(node);
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
}
