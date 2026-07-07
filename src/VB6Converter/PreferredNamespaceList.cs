using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace VB6Converter;

/// <summary>
/// A collection object to encapsulate namespace preferences, as used by AmbiguousTypeQualifier.
/// </summary>
public class PreferredNamespaceList : List<string>
{
    public PreferredNamespaceList() { }

    public PreferredNamespaceList(IEnumerable<string> namespaces) : base(namespaces) { }

    public ITypeSymbol PickType(IEnumerable<ITypeSymbol> candidates, ITypeSymbol contextualType = null)
    {
        // 0. Contextual type: assignment LHS, variable initializer, return/parameter type
        if (contextualType is not null) {
            var contextMatch = candidates.FirstOrDefault(c =>
                SymbolEqualityComparer.Default.Equals(c, contextualType));
            if (contextMatch is not null) {
                return contextMatch;
            }
        }

        // 1. User-preferred namespace prefixes, in order
        foreach (var preferred in this) {
            foreach (var candidate in candidates) {
                var ns = candidate.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (ns == preferred || ns.StartsWith(preferred + ".", StringComparison.Ordinal)) {
                    return candidate;
                }
            }
        }

        // 2. Implied preferences
        foreach (var implied in new string[] { "VB", "VBA", "VBRUN", "System" }) {
            foreach (var candidate in candidates) {
                var ns = candidate.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (ns == implied || ns.StartsWith(implied + ".", StringComparison.Ordinal)) {
                    return candidate;
                }
            }
        }

        // 3. First candidate (Roslyn-defined order)
        return candidates.FirstOrDefault();
    }
}
