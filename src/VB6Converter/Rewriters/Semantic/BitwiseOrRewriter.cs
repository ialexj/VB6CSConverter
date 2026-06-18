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
                var leftType  = semantics.GetTypeInfo(node.Left).Type;
                var rightType = semantics.GetTypeInfo(node.Right).Type;

                if (leftType?.TypeKind == TypeKind.Enum && rightType?.TypeKind == TypeKind.Enum)
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

            if (operandType is null || operandType.SpecialType == SpecialType.System_Boolean)
                return base.VisitPrefixUnaryExpression(node);

            var newToken = Token(
                node.OperatorToken.LeadingTrivia,
                SyntaxKind.TildeToken,
                node.OperatorToken.TrailingTrivia);

            var visitedOperand = (ExpressionSyntax)Visit(node.Operand)!;

            return PrefixUnaryExpression(SyntaxKind.BitwiseNotExpression, newToken, visitedOperand);
        });
}
