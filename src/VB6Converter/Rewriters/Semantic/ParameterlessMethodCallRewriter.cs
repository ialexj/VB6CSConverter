using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Rewrites bare member access to a parameterless call when semantic binding resolves the member to a
/// value-returning zero-argument method. This is separate from MemberFinder because it changes call shape,
/// not member naming.
/// </summary>
public class ParameterlessMethodCallRewriter(SemanticModel sem) : LoggedRewriter
{
    static bool IsParameterlessValueMethod(ISymbol? member)
        => member is IMethodSymbol { Parameters.Length: 0, ReturnsVoid: false };

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Rewrite(node, node => {
            var symbol = sem.GetSymbolInfo(node).Symbol;
            if (!IsParameterlessValueMethod(symbol)) {
                return base.VisitMemberAccessExpression(node);
            }

            if (node.Parent is InvocationExpressionSyntax) {
                return base.VisitMemberAccessExpression(node);
            }

            if (node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node) {
                return base.VisitMemberAccessExpression(node);
            }

            return InvocationExpression(node);
        });
}