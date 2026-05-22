using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Rewrites numeric literals in assignments where the LHS has a concrete type that
/// requires an explicit literal form:
/// <list type="bullet">
/// <item><c>bool</c>  — integer <c>0</c> → <c>false</c>, non-zero integer → <c>true</c></item>
/// <item><c>decimal</c> — bare double/int literal → suffix with <c>M</c> (e.g. <c>8.25</c> → <c>8.25M</c>)</item>
/// <item><c>float</c>  — bare double/int literal → suffix with <c>F</c> (e.g. <c>8.25</c> → <c>8.25F</c>)</item>
/// </list>
/// VB6's ValueConverter always emits floating-point values as bare C# <c>double</c> literals,
/// causing compile errors for <c>decimal</c>/<c>float</c> properties. VB6 also uses <c>-1</c>/<c>0</c>
/// for <c>True</c>/<c>False</c>, which are not valid C# <c>bool</c> assignments.
/// </summary>
public class LiteralCoercionRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node =>
        {
            var lhsType = semantics.GetTypeInfo(node.Left).Type;
            if (lhsType is null)
                return base.VisitAssignmentExpression(node);

            var newRight = lhsType.SpecialType switch
            {
                SpecialType.System_Boolean => CoerceToBool(node.Right),
                SpecialType.System_Decimal => CoerceNumericLiteral(node.Right,
                    v => v is double or int,
                    text => Literal(decimal.Parse(text))),
                SpecialType.System_Single  => CoerceNumericLiteral(node.Right,
                    v => v is double or int,
                    text => Literal(float.Parse(text))),
                _ => node.Right
            };

            if (!ReferenceEquals(newRight, node.Right))
                return node.WithRight(newRight);

            return base.VisitAssignmentExpression(node);
        });

    private static ExpressionSyntax CoerceToBool(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit
            && lit.IsKind(SyntaxKind.NumericLiteralExpression)
            && lit.Token.Value is int iv)
        {
            var kwKind   = iv == 0 ? SyntaxKind.FalseKeyword           : SyntaxKind.TrueKeyword;
            var exprKind = iv == 0 ? SyntaxKind.FalseLiteralExpression  : SyntaxKind.TrueLiteralExpression;
            return LiteralExpression(exprKind,
                Token(lit.Token.LeadingTrivia, kwKind, lit.Token.TrailingTrivia));
        }

        // VB6 True = -1: unary minus wrapping a non-zero integer literal → true
        if (expr is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.UnaryMinusExpression)
            && unary.Operand is LiteralExpressionSyntax inner
            && inner.IsKind(SyntaxKind.NumericLiteralExpression)
            && inner.Token.Value is int innerVal && innerVal != 0)
        {
            return LiteralExpression(SyntaxKind.TrueLiteralExpression,
                Token(unary.OperatorToken.LeadingTrivia, SyntaxKind.TrueKeyword, inner.Token.TrailingTrivia));
        }

        return expr;
    }

    /// <summary>
    /// Replaces a numeric literal (or unary-minus wrapping one) when <paramref name="shouldCoerce"/>
    /// is true for the token's value. Preserves the original token's leading/trailing trivia.
    /// Uses the token <em>text</em> (not the boxed value) to avoid floating-point precision loss
    /// when parsing to <c>decimal</c> or <c>float</c>.
    /// </summary>
    private static ExpressionSyntax CoerceNumericLiteral(
        ExpressionSyntax expr,
        Func<object, bool> shouldCoerce,
        Func<string, SyntaxToken> makeToken)
    {
        if (expr is LiteralExpressionSyntax lit
            && lit.IsKind(SyntaxKind.NumericLiteralExpression)
            && lit.Token.Value is object tokenVal
            && shouldCoerce(tokenVal))
        {
            var newToken = makeToken(lit.Token.Text)
                .WithLeadingTrivia(lit.Token.LeadingTrivia)
                .WithTrailingTrivia(lit.Token.TrailingTrivia);
            return lit.WithToken(newToken);
        }

        if (expr is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            var newOperand = CoerceNumericLiteral(unary.Operand, shouldCoerce, makeToken);
            if (!ReferenceEquals(newOperand, unary.Operand))
                return unary.WithOperand((ExpressionSyntax)newOperand);
        }

        return expr;
    }
}
