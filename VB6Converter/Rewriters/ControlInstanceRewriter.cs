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
    bool IsControl(SimpleNameSyntax name) => name is not null && controls.Contains(name.Identifier.Text);

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (IsControl(node.Left as SimpleNameSyntax) 
            && node.Right is LiteralExpressionSyntax literal 
            && literal.Token.IsKind(SyntaxKind.NullLiteralExpression)) {

            return InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleAssignmentExpression, 
                    MemberAccessExpression(SyntaxKind.SimpleAssignmentExpression, node.Left, IdentifierName("_Instance")),
                    IdentifierName("Close")),
                ArgumentList()
            );
        }

        return base.VisitAssignmentExpression(node);
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax macc && macc.Name.Identifier.ValueText == "Show") {
            var names = macc.EnumerateNames().ToArray();
            if (IsControl(names[^1])) {
                macc = InsertInstance(macc);
                
                if (node.ArgumentList.Arguments.Any(a => a.Expression is SimpleNameSyntax name && Equals(name.Identifier.ValueText, "vbModal"))) {
                    return InvocationExpression(
                        macc.WithName(IdentifierName("ShowDialog")),
                        ArgumentList());
                }
                else {
                    return node.WithExpression(macc);
                }
            }
        } 

        return base.VisitInvocationExpression(node);
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        return base.VisitMemberAccessExpression(InsertInstance(node));
    }

    private MemberAccessExpressionSyntax InsertInstance(MemberAccessExpressionSyntax node)
    {
        var expressions = node.EnumerateNames().ToArray();
        if (expressions.Length >= 1) {
            var root = expressions[^1];
            if (controls.Contains(root.Identifier.Text) && !expressions.Any(e => e.Identifier.Text == "_Instance")) {
                return node.ReplaceNode(root, MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    root, IdentifierName("_Instance")));
            }
        }

        return node;
    }
}
