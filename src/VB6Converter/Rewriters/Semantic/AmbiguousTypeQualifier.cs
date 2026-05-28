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
///   <item>Contextual type inferred from surrounding syntax: assignment LHS, variable
///     initializer, enclosing return type, or method parameter type.</item>
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

        var contextualType = TryGetContextualType(node);
        var chosen = PickType(typeSymbols, contextualType);
        var fqn = chosen.ToString();
        var original = node.ToString();

        if (string.IsNullOrEmpty(fqn) || fqn == original) {
            return null;
        }

        return ParseName(fqn).WithTriviaFrom(node);
    }

    /// <summary>
    /// Walks the parent chain of <paramref name="node"/> looking for syntax contexts
    /// that constrain the expected type: variable declaration, assignment LHS, enclosing
    /// return type, or method-call parameter type.
    /// </summary>
    ITypeSymbol TryGetContextualType(NameSyntax node)
    {
        for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent) {
            switch (ancestor) {
                // Variable declaration where this node IS the declared type:
                //   Widget x = expr  →  infer from initializer expression
                case VariableDeclarationSyntax decl when decl.Type.Span.Contains(node.Span):
                    foreach (var variable in decl.Variables) {
                        if (variable.Initializer?.Value is { } init) {
                            var t = sem.GetTypeInfo(init).Type;
                            if (t is not null) return t;
                        }
                    }
                    return null;

                // Variable declaration where this node is in the initializer:
                //   A.Widget x = new Widget()  →  infer from the declared type
                case VariableDeclarationSyntax decl:
                    return sem.GetTypeInfo(decl.Type).Type;

                // Assignment RHS:  x = new Widget()  →  infer from LHS
                case AssignmentExpressionSyntax assign when assign.Right.Span.Contains(node.Span):
                    return sem.GetTypeInfo(assign.Left).Type;

                // Return statement:  return new Widget()  →  enclosing method/property return type
                case ReturnStatementSyntax:
                    return sem.GetEnclosingSymbol(node.SpanStart) switch {
                        IMethodSymbol   m => m.ReturnType,
                        IPropertySymbol p => p.Type,
                        _ => null
                    };

                // Method argument:  Foo(new Widget())  →  matching parameter type
                case ArgumentSyntax arg
                    when arg.Parent is ArgumentListSyntax argList
                      && argList.Parent is InvocationExpressionSyntax invoc: {
                    var si = sem.GetSymbolInfo(invoc);
                    if ((si.Symbol ?? si.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method) {
                        int index = argList.Arguments.IndexOf(arg);
                        IParameterSymbol param = arg.NameColon is { } nc
                            ? method.Parameters.FirstOrDefault(p => p.Name == nc.Name.Identifier.Text)
                            : index >= 0 && index < method.Parameters.Length ? method.Parameters[index] : null;
                        if (param is not null) return param.Type;
                    }
                    return null;
                }

                // Stop at class / compilation-unit boundaries
                case MemberDeclarationSyntax:
                case CompilationUnitSyntax:
                    return null;
            }
        }
        return null;
    }

    ITypeSymbol PickType(IReadOnlyList<ITypeSymbol> candidates, ITypeSymbol contextualType = null)
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
        foreach (var preferred in _preferredNamespaces) {
            foreach (var candidate in candidates) {
                var ns = candidate.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (ns == preferred || ns.StartsWith(preferred + ".", StringComparison.Ordinal)) {
                    return candidate;
                }
            }
        }

        // 2. Implied preferences
        foreach (var implied in new string[] { "VB", "VBRUN" }) {
            foreach (var candidate in candidates) {
                var ns = candidate.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (ns == implied || ns.StartsWith(implied + ".", StringComparison.Ordinal)) {
                    return candidate;
                }
            }
        }

        // 3. Any type in the System.* namespace hierarchy
        var systemType = candidates.FirstOrDefault(c => {
            var ns = c.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
        });
        if (systemType != null) {
            return systemType;
        }

        // 4. First candidate (Roslyn-defined order)
        return candidates[0];
    }
}
