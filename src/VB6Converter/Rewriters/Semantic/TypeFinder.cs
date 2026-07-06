using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace VB6Converter.Rewriters.Semantic;

public class TypeFinder(SemanticModel sem) : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is VariableDeclarationSyntax || node.Parent is ParameterSyntax) {
                if (sem.GetSymbolInfo(node).Symbol is ITypeSymbol) {
                    return base.VisitIdentifierName(node);
                }
                var type = RoslynHelpers.FindTypeByName(sem, node.Identifier.Text);
                if (type != null && !string.Equals(type.ToString(), node.Identifier.Text, StringComparison.Ordinal)) {
                    return SyntaxFactory.ParseName(type.ToString());
                }
            }

            return base.VisitIdentifierName(node);
        });

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is VariableDeclarationSyntax || node.Parent is ParameterSyntax) {
                if (sem.GetSymbolInfo(node).Symbol is ITypeSymbol) {
                    return node;
                }
                var type = RoslynHelpers.FindTypeByName(sem, node.ToString());
                if (type != null && !string.Equals(type.ToString(), node.ToString(), StringComparison.Ordinal)) {
                    return SyntaxFactory.ParseName(type.ToString());
                }
            }

            return node; // don't recurse
        });
}
