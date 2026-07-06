#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
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

    // A bare reference to a method (no invocation) is invalid in most value contexts (e.g. `obj.Count == 0`),
    // so the compiler fails to bind it and reports the method only via CandidateSymbols, not Symbol. If the
    // declaring type also implements IEnumerable<T>, a LINQ extension method (e.g. Enumerable.Count) could in
    // theory also be a candidate - sort so a genuinely declared instance method wins over an extension, but
    // still fall back to the extension if that's all there is (COM stubs are sometimes extended this way).
    static ISymbol? ResolveCandidate(SymbolInfo info)
        => info.Symbol
        ?? info.CandidateSymbols
            .Where(IsParameterlessValueMethod)
            .Cast<IMethodSymbol>()
            .OrderBy(m => m.IsExtensionMethod)
            .Cast<ISymbol>()
            .FirstOrDefault();

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Rewrite(node, node => {
            var symbol = ResolveCandidate(sem.GetSymbolInfo(node));
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
