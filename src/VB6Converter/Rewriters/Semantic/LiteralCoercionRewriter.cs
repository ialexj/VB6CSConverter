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
                SpecialType.System_Int32   => CoerceUIntToInt(node.Right),
                SpecialType.System_UInt32  => CoerceIntToUInt(node.Right),
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
    /// When the RHS is a <c>uint</c> numeric literal (e.g. a hex literal like <c>0x80000010</c>
    /// that overflows <c>int</c>) and the LHS is <c>int</c>, rewrite the literal to its
    /// signed-integer equivalent.  For all values except <c>int.MinValue</c> this produces a
    /// unary-minus expression wrapping the absolute value (e.g. <c>-2147483632</c>).  For the
    /// single special case of <c>0x80000000</c> it emits the identifier <c>int.MinValue</c>.
    /// </summary>
    private static ExpressionSyntax CoerceUIntToInt(ExpressionSyntax expr)
    {
        if (expr is not LiteralExpressionSyntax lit
            || !lit.IsKind(SyntaxKind.NumericLiteralExpression)
            || lit.Token.Value is not uint uintVal)
            return expr;

        var intVal = unchecked((int)uintVal);

        if (intVal == int.MinValue)
        {
            // -(2147483648) would make the inner literal uint, giving a long result.
            // Emit int.MinValue instead.
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(lit.Token.LeadingTrivia, SyntaxKind.IntKeyword, TriviaList())),
                IdentifierName(Identifier(TriviaList(), "MinValue", lit.Token.TrailingTrivia)));
        }

        // intVal is always negative here: uintVal > int.MaxValue implies intVal < 0.
        // -intVal fits in int because intVal != int.MinValue.
        var absLit = LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            Literal(-intVal).WithTrailingTrivia(lit.Token.TrailingTrivia));

        return PrefixUnaryExpression(
            SyntaxKind.UnaryMinusExpression,
            Token(lit.Token.LeadingTrivia, SyntaxKind.MinusToken, TriviaList()),
            absLit);
    }

    /// <summary>
    /// Rewrites a negative int literal (or unary-minus wrapping a positive literal) to its
    /// unsigned 32-bit equivalent when the LHS is <c>uint</c>.  For example <c>-2147483632</c>
    /// becomes <c>2147483664</c> and <c>-1</c> becomes <c>4294967295</c>.
    /// </summary>
    private static ExpressionSyntax CoerceIntToUInt(ExpressionSyntax expr)
    {
        if (expr is not PrefixUnaryExpressionSyntax unary
            || !unary.IsKind(SyntaxKind.UnaryMinusExpression)
            || unary.Operand is not LiteralExpressionSyntax inner
            || !inner.IsKind(SyntaxKind.NumericLiteralExpression))
            return expr;

        uint uintVal;
        if (inner.Token.Value is int posInt && posInt > 0)
            uintVal = unchecked((uint)(-posInt));
        else if (inner.Token.Value is uint posUint)
            // Handles -(2147483648) where the inner literal is already uint.
            uintVal = unchecked((uint)(-(long)posUint));
        else
            return expr;

        var newToken = Literal(uintVal.ToString(), uintVal)
            .WithLeadingTrivia(unary.OperatorToken.LeadingTrivia)
            .WithTrailingTrivia(inner.Token.TrailingTrivia);
        return LiteralExpression(SyntaxKind.NumericLiteralExpression, newToken);
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
