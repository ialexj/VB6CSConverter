using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Rewrites reference-type comparisons with <c>default</c> to pattern-matching null checks.
/// <list type="bullet">
/// <item><c>expr == default</c> → <c>expr is null</c></item>
/// <item><c>expr != default</c> → <c>expr is not null</c></item>
/// </list>
/// Only applied when the non-<c>default</c> operand resolves to a reference type or <c>dynamic</c>.
/// Value-type comparisons are left unchanged.
/// </summary>
public class DefaultToNullRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node =>
        {
            if (!node.IsKind(SyntaxKind.EqualsExpression) && !node.IsKind(SyntaxKind.NotEqualsExpression))
                return base.VisitBinaryExpression(node);

            // Determine which operand is `default` and which is the subject expression.
            ExpressionSyntax subject;
            SyntaxToken defaultToken;

            if (node.Right.IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                subject      = node.Left;
                defaultToken = ((LiteralExpressionSyntax)node.Right).Token;
            }
            else if (node.Left.IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                subject      = node.Right;
                defaultToken = ((LiteralExpressionSyntax)node.Left).Token;
            }
            else
            {
                return base.VisitBinaryExpression(node);
            }

            // Only rewrite for reference types (includes dynamic; excludes value types).
            var type = semantics.GetTypeInfo(subject).Type;
            if (type is null || !type.IsReferenceType)
                return base.VisitBinaryExpression(node);

            // Recursively process the subject for any nested rewrites.
            var visitedSubject = (ExpressionSyntax)Visit(subject)!;

            // Transfer operator trivia to the `is` keyword.
            var isKeyword = Token(
                node.OperatorToken.LeadingTrivia,
                SyntaxKind.IsKeyword,
                node.OperatorToken.TrailingTrivia);

            // `null` gets the trailing trivia from the original `default` token.
            var nullLiteral = LiteralExpression(
                SyntaxKind.NullLiteralExpression,
                Token(TriviaList(), SyntaxKind.NullKeyword, defaultToken.TrailingTrivia));

            if (node.IsKind(SyntaxKind.EqualsExpression))
            {
                // expr == default  →  expr is null
                return IsPatternExpression(visitedSubject, isKeyword, ConstantPattern(nullLiteral));
            }
            else
            {
                // expr != default  →  expr is not null
                var notKeyword = Token(TriviaList(), SyntaxKind.NotKeyword, TriviaList(Whitespace(" ")));

                return IsPatternExpression(
                    visitedSubject,
                    isKeyword,
                    UnaryPattern(notKeyword, ConstantPattern(nullLiteral)));
            }
        });
}
