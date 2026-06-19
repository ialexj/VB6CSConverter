using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class ErrRaiseRewriter(string file = null) : LoggedRewriter(file)
{
    public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is not InvocationExpressionSyntax invocation
                || !TryGetErrRaiseArguments(invocation, out var args)) {
                return base.VisitExpressionStatement(node);
            }

            if (IsCanonicalReRaise(args)) {
                return ThrowStatement();
            }

            return BuildThrowBlock(args);
        });

    static bool TryGetErrRaiseArguments(InvocationExpressionSyntax invocation, out SeparatedSyntaxList<ArgumentSyntax> args)
    {
        args = default;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) {
            return false;
        }

        if (!string.Equals(memberAccess.Name.Identifier.Text, "Raise", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!IsErrObjectExpression(memberAccess.Expression)) {
            return false;
        }

        args = invocation.ArgumentList.Arguments;
        return true;
    }

    static bool IsCanonicalReRaise(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        if (args.Count != 5) {
            return false;
        }

        return IsErrMemberArgument(args[0].Expression, "Number")
            && IsErrMemberArgument(args[1].Expression, "Source")
            && IsErrMemberArgument(args[2].Expression, "Description")
            && IsErrMemberArgument(args[3].Expression, "HelpFile")
            && IsErrMemberArgument(args[4].Expression, "HelpContext");
    }

    static bool IsErrMemberArgument(ExpressionSyntax expression, string memberName)
    {
        return expression is MemberAccessExpressionSyntax memberAccess
            && string.Equals(memberAccess.Name.Identifier.Text, memberName, StringComparison.OrdinalIgnoreCase)
            && IsErrObjectExpression(memberAccess.Expression);
    }

    static bool IsErrObjectExpression(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax identifier) {
            return string.Equals(identifier.Identifier.Text, "Err", StringComparison.OrdinalIgnoreCase);
        }

        if (expression is InvocationExpressionSyntax invocation
            && invocation.ArgumentList.Arguments.Count == 0
            && invocation.Expression is IdentifierNameSyntax invokedIdentifier) {
            return string.Equals(invokedIdentifier.Identifier.Text, "Err", StringComparison.OrdinalIgnoreCase);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess
            && string.Equals(memberAccess.Name.Identifier.Text, "Err", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return false;
    }

    static ThrowStatementSyntax BuildThrowBlock(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var message = GetArgumentExpressionOrDefault(args, 2,
            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("")));

        List<ExpressionSyntax> dataItems = [];

        AddDataAssignment(dataItems, "Code", GetArgumentExpression(args, 0));
        AddDataAssignment(dataItems, "Source", GetArgumentExpression(args, 1));
        AddDataAssignment(dataItems, "HelpFile", GetArgumentExpression(args, 3));
        AddDataAssignment(dataItems, "HelpContext", GetArgumentExpression(args, 4));

        var exception = ObjectCreationExpression(ParseTypeName("System.Exception"))
            .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(message))));

        if (dataItems.Count > 0) {
            var dataInitializer = AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                IdentifierName("Data"),
                InitializerExpression(
                    SyntaxKind.CollectionInitializerExpression,
                    SeparatedList(dataItems)));

            exception = exception.WithInitializer(
                InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SingletonSeparatedList<ExpressionSyntax>(dataInitializer)));
        }

        return ThrowStatement(exception);
    }

    static ExpressionSyntax GetArgumentExpressionOrDefault(SeparatedSyntaxList<ArgumentSyntax> args, int index, ExpressionSyntax fallback)
    {
        return GetArgumentExpression(args, index) ?? fallback;
    }

    static ExpressionSyntax GetArgumentExpression(SeparatedSyntaxList<ArgumentSyntax> args, int index)
    {
        if (index < 0 || index >= args.Count) {
            return null;
        }

        var expression = args[index].Expression;
        return expression;
    }

    static void AddDataAssignment(List<ExpressionSyntax> dataItems, string key, ExpressionSyntax value)
    {
        if (value is null) {
            return;
        }

        dataItems.Add(
            AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                ImplicitElementAccess(
                    BracketedArgumentList(
                        SingletonSeparatedList(
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(key)))))),
                value));
    }
}
