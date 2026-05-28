using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Rewrites setter call sites that originate from VB6 multi-value properties.
/// The initial conversion emits <c>obj.Foo[k1, k2] = v</c> (element access assignment) for both
/// array indexing and parameterized property setters. This rewriter detects the latter case by
/// checking whether a corresponding <c>SetFoo</c> method exists and, if so, rewrites to
/// <c>obj.SetFoo(k1, k2, v)</c>.
/// </summary>
public class ParameterizedPropertyRewriter(SemanticModel model) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node =>
        {
            if (node.Left is not ElementAccessExpressionSyntax ea)
                return base.VisitAssignmentExpression(node);

            // If the expression already resolves (e.g. a real array field), leave it alone.
            var symbol = model.GetSymbolInfo(ea.Expression);
            if (symbol.Symbol is not null || !symbol.CandidateSymbols.IsEmpty)
                return base.VisitAssignmentExpression(node);

            // Extract receiver and member name.
            string memberName;
            ExpressionSyntax receiver;
            if (ea.Expression is MemberAccessExpressionSyntax ma)
            {
                memberName = ma.Name.Identifier.Text;
                receiver = ma.Expression;
            }
            else if (ea.Expression is IdentifierNameSyntax id)
            {
                memberName = id.Identifier.Text;
                receiver = null;
            }
            else
            {
                return base.VisitAssignmentExpression(node);
            }

            var setName = "Set" + memberName;

            // Look for a Set{Name} method.
            IMethodSymbol setMethod;
            if (receiver is not null)
            {
                var targetType = model.GetTypeInfo(receiver).Type;
                if (targetType is null)
                    return base.VisitAssignmentExpression(node);
                setMethod = targetType.GetMembers(setName).OfType<IMethodSymbol>().FirstOrDefault();
            }
            else
            {
                setMethod = model.LookupSymbols(ea.SpanStart, name: setName)
                    .OfType<IMethodSymbol>().FirstOrDefault();
            }

            if (setMethod is null)
                return base.VisitAssignmentExpression(node);

            // Rewrite: receiver.SetName(indexArgs..., value)
            var allArgs = ea.ArgumentList.Arguments.Add(Argument(node.Right));
            var setCallTarget = receiver is not null
                ? (ExpressionSyntax)MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, receiver, IdentifierName(setName))
                : IdentifierName(setName);

            return InvocationExpression(setCallTarget, ArgumentList(allArgs));
        });
}
