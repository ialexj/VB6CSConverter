using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class ControlInstanceRewriter(IEnumerable<string> controls) : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        static IEnumerable<SimpleNameSyntax> EnumerateNames(ExpressionSyntax expression)
        {
            if (expression is MemberAccessExpressionSyntax inner) {
                yield return inner.Name;
                foreach (var expr in EnumerateNames(inner.Expression)) {
                    yield return expr;
                }
            }
            else if (expression is ElementAccessExpressionSyntax element) {
                foreach (var expr in EnumerateNames(element.Expression)) {
                    yield return expr;
                }
            }
            else if (expression is SimpleNameSyntax simple) {
                yield return simple;
            }
        }

        var expressions = EnumerateNames(node).ToArray();
        if (expressions.Length >= 1) {
            var root = expressions[^1];
            if (controls.Contains(root.Identifier.Text) && !expressions.Any(e => e.Identifier.Text == "_Instance")) {
                return node.ReplaceNode(root, MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    root, IdentifierName("_Instance")));
            }
        }
        
        return base.VisitMemberAccessExpression(node);
    }
}
