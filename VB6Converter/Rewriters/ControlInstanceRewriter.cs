using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class ControlInstanceRewriter(IEnumerable<string> controls, string self) : LoggedRewriter
{
    bool IsControl(SimpleNameSyntax name) => name is not null && controls.Contains(name.Identifier.Text);

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node => {
            if (IsControl(node.Left as SimpleNameSyntax)
                && node.Right is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.NullLiteralExpression)) {

                if (node.Left.ToString() == self) {
                    return InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("Close")), 
                        ArgumentList());
                }
                else {
                    return InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, node.Left, IdentifierName("_Instance")),
                            IdentifierName("Close")),
                        ArgumentList());
                }
            }

            return base.VisitAssignmentExpression(node);
        });

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is MemberAccessExpressionSyntax macc && macc.Name.Identifier.ValueText == "Show") {
                var names = macc.EnumerateNames().ToArray();
                if (IsControl(names[^1])) {
                    macc = InsertInstance(macc);
                    return node.WithExpression(macc);
                }
            }

            return base.VisitInvocationExpression(node);
        });

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Rewrite(node, node => {
            return base.VisitMemberAccessExpression(InsertInstance(node));
        });

    MemberAccessExpressionSyntax InsertInstance(MemberAccessExpressionSyntax node)
    {
        var expressions = node.EnumerateNames().ToArray();
        if (expressions.Length >= 1) {
            var root = expressions[^1];

            if (self == root.Identifier.Text) {
                return node.ReplaceNode(root, ThisExpression());
            }
            else if (controls.Contains(root.Identifier.Text) && !expressions.Any(e => e.Identifier.Text == "_Instance")) {
                return node.ReplaceNode(root, MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    root, IdentifierName("_Instance")));
            }
        }

        return node;
    }
}
