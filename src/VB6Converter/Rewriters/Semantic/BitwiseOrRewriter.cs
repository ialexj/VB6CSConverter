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
            if (!node.IsKind(SyntaxKind.LogicalOrExpression) && !node.IsKind(SyntaxKind.LogicalAndExpression))
                return base.VisitBinaryExpression(node);

            var leftType  = semantics.GetTypeInfo(node.Left).Type;
            var rightType = semantics.GetTypeInfo(node.Right).Type;

            if (leftType is null || rightType is null)
                return base.VisitBinaryExpression(node);

            if (leftType.SpecialType == SpecialType.System_Boolean || rightType.SpecialType == SpecialType.System_Boolean)
                return base.VisitBinaryExpression(node);

            var (newKind, tokenKind) = node.IsKind(SyntaxKind.LogicalOrExpression)
                ? (SyntaxKind.BitwiseOrExpression,  SyntaxKind.BarToken)
                : (SyntaxKind.BitwiseAndExpression, SyntaxKind.AmpersandToken);

            var newOperator  = Token(
                node.OperatorToken.LeadingTrivia,
                tokenKind,
                node.OperatorToken.TrailingTrivia);

            // Visit children so nested logical operators are also converted.
            var visitedLeft  = (ExpressionSyntax)Visit(node.Left)!;
            var visitedRight = (ExpressionSyntax)Visit(node.Right)!;

            return BinaryExpression(newKind, visitedLeft, newOperator, visitedRight);
        });
}
