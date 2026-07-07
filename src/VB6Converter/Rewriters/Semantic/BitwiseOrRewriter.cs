using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

public class BitwiseOrRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node =>
        {
            // VB6 enum flag combining: enum + enum → enum | enum
            if (node.IsKind(SyntaxKind.AddExpression))
            {
                // Chained "A + B + C" parses as "(A + B) + C". Since "enum + enum" has no
                // valid operator in real C#, the semantic model can't type the inner
                // "(A + B)" node directly, so recurse through nested Add expressions to
                // find the underlying enum operands.
                if (IsEnumTyped(node.Left) && IsEnumTyped(node.Right))
                {
                    var newOperator = Token(
                        node.OperatorToken.LeadingTrivia,
                        SyntaxKind.BarToken,
                        node.OperatorToken.TrailingTrivia);

                    // Visit children so chained a + b + c → a | b | c
                    var visitedLeft  = (ExpressionSyntax)Visit(node.Left)!;
                    var visitedRight = (ExpressionSyntax)Visit(node.Right)!;

                    return BinaryExpression(SyntaxKind.BitwiseOrExpression, visitedLeft, newOperator, visitedRight);
                }

                return base.VisitBinaryExpression(node);
            }

            if (!node.IsKind(SyntaxKind.LogicalOrExpression) && !node.IsKind(SyntaxKind.LogicalAndExpression))
                return base.VisitBinaryExpression(node);

            var lType  = semantics.GetTypeInfo(node.Left).Type;
            var rType = semantics.GetTypeInfo(node.Right).Type;

            if (lType is null || rType is null)
                return base.VisitBinaryExpression(node);

            // "dynamic" operands aren't confirmed non-boolean - the semantic model simply
            // couldn't determine a concrete type, so don't assume a bitwise rewrite is safe.
            if (lType.TypeKind == TypeKind.Dynamic || rType.TypeKind == TypeKind.Dynamic)
                return base.VisitBinaryExpression(node);

            if (lType.SpecialType == SpecialType.System_Boolean || rType.SpecialType == SpecialType.System_Boolean)
                return base.VisitBinaryExpression(node);

            var (newKind, tokenKind) = node.IsKind(SyntaxKind.LogicalOrExpression)
                ? (SyntaxKind.BitwiseOrExpression,  SyntaxKind.BarToken)
                : (SyntaxKind.BitwiseAndExpression, SyntaxKind.AmpersandToken);

            var newOp  = Token(
                node.OperatorToken.LeadingTrivia,
                tokenKind,
                node.OperatorToken.TrailingTrivia);

            // Visit children so nested logical operators are also converted.
            var visitLeft  = (ExpressionSyntax)Visit(node.Left)!;
            var visitRight = (ExpressionSyntax)Visit(node.Right)!;

            return BinaryExpression(newKind, visitLeft, newOp, visitRight);
        });

    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => Rewrite(node, node =>
        {
            // VB6 `Not intExpr` is bitwise complement; convert `!x` → `~x` for non-boolean operands.
            if (!node.IsKind(SyntaxKind.LogicalNotExpression))
                return base.VisitPrefixUnaryExpression(node);

            var operandType = semantics.GetTypeInfo(node.Operand).Type;

            // "dynamic" operands aren't confirmed non-boolean - the semantic model simply
            // couldn't determine a concrete type, so don't assume a bitwise rewrite is safe.
            if (operandType is null
                || operandType.SpecialType == SpecialType.System_Boolean
                || operandType.TypeKind == TypeKind.Dynamic)
                return base.VisitPrefixUnaryExpression(node);

            var newToken = Token(
                node.OperatorToken.LeadingTrivia,
                SyntaxKind.TildeToken,
                node.OperatorToken.TrailingTrivia);

            var visitedOperand = (ExpressionSyntax)Visit(node.Operand)!;

            return PrefixUnaryExpression(SyntaxKind.BitwiseNotExpression, newToken, visitedOperand);
        });

    // Determines whether an expression is enum-typed, recursing through nested
    // "A + B" chains whose intermediate nodes the semantic model can't type directly
    // (since "enum + enum" has no valid operator in real C#).
    private bool IsEnumTyped(ExpressionSyntax expr)
    {
        var type = semantics.GetTypeInfo(expr).Type;
        if (type?.TypeKind == TypeKind.Enum)
            return true;

        return expr is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.AddExpression)
            && IsEnumTyped(binary.Left)
            && IsEnumTyped(binary.Right);
    }
}
