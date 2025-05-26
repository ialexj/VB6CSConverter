using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace VB6Converter.Rewriters.Semantic;

public class TypeFinder(SemanticModel sem, string defaultNamespace) : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Log.Rewrite(this, node, node => {
            if (node.Parent is VariableDeclarationSyntax || node.Parent is ParameterSyntax) {
                var type = FindType(node.Identifier.Text);
                if (type != null && !string.Equals(type.ToString(), node.Identifier.Text, StringComparison.Ordinal)) {
                    return node.WithIdentifier(SyntaxFactory.Identifier(type.Name))
                        .WithAdditionalAnnotations(new SyntaxAnnotation("Using", type.ContainingNamespace.ToString()));
                }
            }

            return base.VisitIdentifierName(node);
        });

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        => Log.Rewrite(this, node, node => {
            if (node.Parent is VariableDeclarationSyntax || node.Parent is ParameterSyntax) {
                var type = FindType(node.ToString());
                if (type != null && !string.Equals(type.ToString(), node.ToString(), StringComparison.Ordinal)) {
                    return SyntaxFactory.ParseName(type.ToString());
                }
            }

            return node; // don't recurse
        });

    ITypeSymbol FindType(string name, INamespaceSymbol nss = null)
    {
        nss ??= sem.Compilation.GlobalNamespace;

        foreach (var m in nss.GetTypeMembers()) {
            if (string.Equals(m.ToString(), name, StringComparison.OrdinalIgnoreCase)) {
                return m;
            }
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) {
                return m;
            }
        }

        var namespaces = nss.GetNamespaceMembers();
        foreach (var nested in namespaces) {
            if (FindType(name, nested) is ITypeSymbol ts) {
                return ts;
            }
        }

        return null;
    }
}
