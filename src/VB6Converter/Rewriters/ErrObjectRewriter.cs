using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

/// <summary>
/// Rewrites bare references to the VB6 global <c>Err</c>/<c>Erl</c> objects into
/// direct calls to <c>Microsoft.VisualBasic.Information.Err()</c>/<c>.Erl()</c>.
/// Must run after <see cref="ErrRaiseRewriter"/> so <c>Err.Raise</c> statements are
/// converted to throw statements first.
/// </summary>
public class ErrObjectRewriter(string file = null) : LoggedRewriter(file)
{
    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.ArgumentList.Arguments.Count == 0
                && node.Expression is IdentifierNameSyntax identifier
                && TryGetMemberName(identifier.Identifier.Text, out var memberName)) {
                return node.WithExpression(BuildQualifiedName(memberName));
            }

            return base.VisitInvocationExpression(node);
        });

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node) {
                return base.VisitIdentifierName(node);
            }

            if (!TryGetMemberName(node.Identifier.Text, out var memberName)) {
                return base.VisitIdentifierName(node);
            }

            return InvocationExpression(BuildQualifiedName(memberName));
        });

    static bool TryGetMemberName(string identifierText, out string memberName)
    {
        if (string.Equals(identifierText, "Err", StringComparison.OrdinalIgnoreCase)) {
            memberName = "Err";
            return true;
        }

        if (string.Equals(identifierText, "Erl", StringComparison.OrdinalIgnoreCase)) {
            memberName = "Erl";
            return true;
        }

        memberName = null;
        return false;
    }

    static ExpressionSyntax BuildQualifiedName(string memberName)
        => MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParseName("Microsoft.VisualBasic.Information"),
            IdentifierName(memberName));
}
