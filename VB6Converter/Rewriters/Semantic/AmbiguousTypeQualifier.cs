using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Fully qualifies type references that are ambiguous because multiple <c>using</c>
/// directives bring a type with the same simple name into scope.
/// </summary>
/// <remarks>
/// Disambiguation priority:
/// <list type="number">
///   <item>User-preferred namespace prefixes, checked in the supplied order.</item>
///   <item>Any candidate whose containing namespace starts with <c>System</c>.</item>
///   <item>The first candidate returned by Roslyn (undefined order).</item>
/// </list>
/// </remarks>
public class AmbiguousTypeQualifier(SemanticModel sem, IEnumerable<string> preferredNamespaces) : LoggedRewriter
{
    readonly IReadOnlyList<string> _preferredNamespaces = preferredNamespaces?.ToList() ?? [];

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Log.Rewrite(this, node, node => {
            var resolved = TryResolveAmbiguous(node);
            return resolved ?? base.VisitIdentifierName(node);
        });

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        => Log.Rewrite(this, node, node => {
            var resolved = TryResolveAmbiguous(node);
            if (resolved != null) {
                return resolved;
            }
            // Don't recurse: if the qualified name itself isn't ambiguous,
            // its components won't be either.
            return node;
        });

    NameSyntax TryResolveAmbiguous(NameSyntax node)
    {
        var info = sem.GetSymbolInfo(node);
        if (info.CandidateReason != CandidateReason.Ambiguous) {
            return null;
        }

        var typeSymbols = info.CandidateSymbols.OfType<ITypeSymbol>().ToList();
        if (typeSymbols.Count == 0) {
            return null;
        }

        var chosen = PickType(typeSymbols);
        var fqn = chosen.ToString();

        if (string.IsNullOrEmpty(fqn) || fqn == node.ToString()) {
            return null;
        }

        return ParseName(fqn).WithTriviaFrom(node);
    }

    ITypeSymbol PickType(IReadOnlyList<ITypeSymbol> candidates)
    {
        // 1. User-preferred namespace prefixes, in order
        foreach (var preferred in _preferredNamespaces) {
            foreach (var candidate in candidates) {
                var ns = candidate.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (ns == preferred || ns.StartsWith(preferred + ".", StringComparison.Ordinal)) {
                    return candidate;
                }
            }
        }

        // 2. Any type in the System.* namespace hierarchy
        var systemType = candidates.FirstOrDefault(c => {
            var ns = c.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
        });
        if (systemType != null) {
            return systemType;
        }

        // 3. First candidate (Roslyn-defined order)
        return candidates[0];
    }
}
