using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

public class DateTimeArithmeticRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node => {
            if (!node.IsKind(SyntaxKind.AddExpression) && !node.IsKind(SyntaxKind.SubtractExpression)) {
                return base.VisitBinaryExpression(node);
            }

            var leftType = semantics.GetTypeInfo(node.Left).Type;
            if (!IsDateTime(leftType)) {
                return base.VisitBinaryExpression(node);
            }

            var rightType = semantics.GetTypeInfo(node.Right).Type;
            if (node.IsKind(SyntaxKind.SubtractExpression) && IsDateTime(rightType)) {
                // Keep DateTime - DateTime as subtraction (TimeSpan result).
                return base.VisitBinaryExpression(node);
            }

            if (!IsNumeric(rightType)) {
                return base.VisitBinaryExpression(node);
            }

            var visitedLeft = ((ExpressionSyntax)Visit(node.Left)!).WithoutTrailingTrivia();
            var visitedRight = ((ExpressionSyntax)Visit(node.Right)!).WithoutLeadingTrivia().WithoutTrailingTrivia();

            ExpressionSyntax daysOffset = visitedRight;
            if (node.IsKind(SyntaxKind.SubtractExpression)) {
                daysOffset = PrefixUnaryExpression(
                    SyntaxKind.UnaryMinusExpression,
                    ParenthesizedExpression(visitedRight));
            }

            return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    visitedLeft,
                    IdentifierName("AddDays")),
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(daysOffset))));
        });

    private static bool IsDateTime(ITypeSymbol type)
        => type?.SpecialType == SpecialType.System_DateTime;

    private static bool IsNumeric(ITypeSymbol type)
        => type?.SpecialType is
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal;
}
