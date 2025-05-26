using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.Rewriters;

public class VBCoreRewriter : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is MemberAccessExpressionSyntax) {
                return base.VisitIdentifierName(node);
            }

            return node.Identifier.Text switch {
                "Now" => ParseExpression("DateTime.Now"),
                "Date" => ParseExpression("DateTime.Now.Date"),
                _ => base.VisitIdentifierName(node),
            };
        });

    static readonly Dictionary<string, Func<InvocationExpressionSyntax, SyntaxNode>> _funcs = new(StringComparer.OrdinalIgnoreCase) {
        ["Array"] = ConvertArray,
        ["Replace"] = ConvertReplace,

        ["IsNull"] = ConvertIsNull,
        ["IsArray"] = node => ConvertIs(node, IdentifierName(nameof(Array))),

        ["UBound"] = ConvertUBound,

        ["DateSerial"] = ConvertDateSerial,

        ["Hour"] = ConvertToMemberAccess,
        ["Minute"] = ConvertToMemberAccess,
        ["Second"] = ConvertToMemberAccess,
        ["Year"] = ConvertToMemberAccess,
        ["Month"] = ConvertToMemberAccess,
        ["Day"] = ConvertToMemberAccess,

        ["String"] = ConvertString,
        ["Len"] = ConvertLen,
        ["Left"] = ConvertLeft,

        ["CStr"] = node => ConvertToMemberAccess(node, "Convert.ToString"),
        ["CLng"] = node => ConvertToMemberAccess(node, "Convert.ToInt32"),
        ["CDbl"] = node => ConvertToMemberAccess(node, "Convert.ToDouble"),

        ["IIf"] = ConvertIIf,

        ["Asc"] = ConvertAsc,
        ["Chr"] = ConvertChr,

        ["Str"] = ConvertStr,

        ["IsMissing"] = ConvertIsMissing
    };

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        => Rewrite(node, node => {
            SyntaxNode newSyntax = node;
            if (node.Expression is IdentifierNameSyntax name) {
                // Try to convert via just the name
                var expr = VisitIdentifierName(name);
                if (!name.IsEquivalentTo(expr)) {
                    return expr;
                }

                if (_funcs.TryGetValue(name.Identifier.Text, out var converter)) {
                    newSyntax = converter(node);
                }
            }

            if (newSyntax?.IsEquivalentTo(node) == false) {
                return Visit(newSyntax);
            }
            else {
                return base.VisitInvocationExpression(node);
            }
        });

    static SyntaxNode ConvertStr(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments[0].Expression;
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(arg), IdentifierName("ToString")),
            ArgumentList());
    }

    static SyntaxNode ConvertIsMissing(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments[0].Expression;
        return BinaryExpression(SyntaxKind.EqualsExpression, arg, LiteralExpression(SyntaxKind.DefaultLiteralExpression));
    }

    static SyntaxNode ConvertChr(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments[0].Expression;
        if (arg is LiteralExpressionSyntax literal 
            && literal.IsKind(SyntaxKind.NumericLiteralExpression)) {
            return LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                Literal(Convert.ToChar((int)literal.Token.Value)));
        }
        else {
            return ParenthesizedExpression(
                CastExpression(PredefinedType(Token(SyntaxKind.CharKeyword)),
                arg));
        }
    }

    static SyntaxNode ConvertString(InvocationExpressionSyntax node)
    {
        return ObjectCreationExpression(
            PredefinedType(Token(SyntaxKind.StringKeyword)),
            ArgumentList(
                node.ArgumentList.Arguments[1].Expression,
                node.ArgumentList.Arguments[0].Expression),
            null);
    }

    static SyntaxNode ConvertArray(InvocationExpressionSyntax node)
    {
        return ImplicitArrayCreationExpression(
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList<ExpressionSyntax>(
                    node.ArgumentList.Arguments.Select(a => (SyntaxNodeOrToken)a.Expression).Intersperse(Token(SyntaxKind.CommaToken))
                )
            )
        );
    }

    static SyntaxNode ConvertAsc(InvocationExpressionSyntax node)
    {
        var args = node.ArgumentList.Arguments[0].Expression;
        if (args is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) 
            && literal.Token.ValueText.Length == 1) {
            return LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                Literal(literal.Token.ValueText[0]));
        }
        else {
            return node;
        }
    }

    static SyntaxNode ConvertIIf(InvocationExpressionSyntax node)
    {
        var condition = node.ArgumentList.Arguments[0].Expression;
        var trueValue = node.ArgumentList.Arguments[1].Expression;
        var falseValue = node.ArgumentList.Arguments[2].Expression;

        return ParenthesizedExpression(ConditionalExpression(condition, trueValue, falseValue));
    }

    static SyntaxNode ConvertToMemberAccess(InvocationExpressionSyntax node, string expression)
        => InvocationExpression(
            ParseExpression(expression),
            ArgumentList(node.ArgumentList.Arguments[0].Expression));

    static SyntaxNode ConvertToMemberAccess(InvocationExpressionSyntax node) 
    {
        if (node.Expression is IdentifierNameSyntax name) {
            var value = node.ArgumentList.Arguments[0];
            return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, value.Expression, name);
        }
        else {
            return node;
        }
    }

    static SyntaxNode ConvertDateSerial(InvocationExpressionSyntax node)
    {
        var year = node.ArgumentList.Arguments[0];
        var month = node.ArgumentList.Arguments[1];
        var day = node.ArgumentList.Arguments[2];

        return ObjectCreationExpression(
            IdentifierName("DateTime"),
            ArgumentList(year, month, day),
            null);
    }

    static SyntaxNode ConvertLen(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];

        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParenthesizedExpression(CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression)),
            IdentifierName("Length"));
    }

    static SyntaxNode ConvertLeft(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];
        var len = node.ArgumentList.Arguments[1];

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression)),
                IdentifierName("Substring")),
            ArgumentList(
                Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                len));        
    }

    static SyntaxNode ConvertUBound(InvocationExpressionSyntax node)
    {
        var array = node.ArgumentList.Arguments[0];

        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParenthesizedExpression(CastExpression(IdentifierName("Array"), array.Expression)),
            IdentifierName("Length"));
    }

    static SyntaxNode ConvertReplace(InvocationExpressionSyntax node)
    {
        var str      = node.ArgumentList.Arguments[0];
        var oldValue = node.ArgumentList.Arguments[1];
        var newValue = node.ArgumentList.Arguments[2];

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression)),
                IdentifierName("Replace")),
            ArgumentList(oldValue, newValue)
        );
    }

    static SyntaxNode ConvertIsNull(InvocationExpressionSyntax node) 
        => ParenthesizedExpression(
            IsPatternExpression(node.ArgumentList.Arguments[0].Expression, ConstantPattern(
                LiteralExpression(
                    SyntaxKind.NullLiteralExpression
                )
            )
        ));

    static SyntaxNode ConvertIsArray(InvocationExpressionSyntax node)
        => ParenthesizedExpression(
            BinaryExpression(
                SyntaxKind.IsExpression,
                node.ArgumentList.Arguments[0].Expression,
                IdentifierName(nameof(Array))));

    static SyntaxNode ConvertIs(InvocationExpressionSyntax node, IdentifierNameSyntax what)
        => ParenthesizedExpression(
            BinaryExpression(
                SyntaxKind.IsExpression,
                node.ArgumentList.Arguments[0].Expression,
                what));
}
