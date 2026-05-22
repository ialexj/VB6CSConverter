using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;
public class TypeCastRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.Right.DescendantNodesAndSelf(n => n is not CastExpressionSyntax).OfType<CastExpressionSyntax>().Any()) {
                return base.VisitAssignmentExpression(node);
            }

            var leftType = semantics.GetTypeInfo(node.Left);
            if (leftType.Type?.SpecialType != SpecialType.System_Object) {
                var rightType = semantics.GetTypeInfo(node.Right);
                if (rightType.Type?.SpecialType == SpecialType.System_Object) {
                    return node.WithRight(ApplyCast(node.Right, leftType.Type));
                }
            }

            return base.VisitAssignmentExpression(node);
        });

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.DescendantNodes(n => n is not CastExpressionSyntax).OfType<CastExpressionSyntax>().Any()) {
                return base.VisitBinaryExpression(node);
            }
            //var diag = semantics.GetDiagnostics(node.Span);
            //if (diag.FirstOrDefault(d => d.Id == "CS0019") is Diagnostic d) {
            //    Debugger.Break();
            //}

            var leftType = semantics.GetTypeInfo(node.Left);
            var rightType = semantics.GetTypeInfo(node.Right);

            if (leftType.Type?.SpecialType == SpecialType.System_Object
                && rightType.Type?.SpecialType != SpecialType.System_Object) {
                
                return node.WithLeft(ApplyCast(node.Left, rightType.Type));
            }
            else if (leftType.Type?.SpecialType != SpecialType.System_Object
                && rightType.Type?.SpecialType == SpecialType.System_Object) {
                return node.WithRight(ApplyCast(node.Right, leftType.Type));
            }

            return base.VisitBinaryExpression(node);
        });

    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalNotExpression) || node.IsKind(SyntaxKind.LogicalOrExpression) || node.IsKind(SyntaxKind.LogicalAndExpression)) {
            var valueType = semantics.GetTypeInfo(node.Operand);
            if (valueType.Type.SpecialType == SpecialType.System_Object) {
                return node.WithOperand(ApplyCast(node.Operand, PredefinedType(Token(SyntaxKind.BoolKeyword))));
            }
        }
        return base.VisitPrefixUnaryExpression(node);
    }

    public override SyntaxNode VisitArgument(ArgumentSyntax node)
    {
        if (node.DescendantNodes(n => n is not CastExpressionSyntax).OfType<CastExpressionSyntax>().Any()) {
            return base.VisitArgument(node);
        }

        // For arguments that are objects
        // that try to be inserted into parameters which are not objects
        // add a cast.

        if (node.Parent is ArgumentListSyntax list && list.Parent is InvocationExpressionSyntax invocation) {
            int index = list.Arguments.IndexOf(node);

            var argumentType = semantics.GetTypeInfo(node.Expression).Type;
            if (argumentType is ITypeSymbol ts && ts.SpecialType == SpecialType.System_Object) {
                var symbol = semantics.GetSymbolInfo(invocation);
                var methodSymbol = symbol.Symbol ?? symbol.CandidateSymbols.FirstOrDefault();

                if (methodSymbol is IMethodSymbol method) {
                    IParameterSymbol parameterSymbol = null;
                    if (node.NameColon != null) {
                        var name = node.NameColon.Name.Identifier.Text;
                        parameterSymbol = method.Parameters.FirstOrDefault(p => p.Name == name);
                    }
                    else if (index >= 0 && index < method.Parameters.Length) {
                        parameterSymbol = method.Parameters[index];
                    }

                    if (parameterSymbol != null && parameterSymbol.Type.SpecialType != SpecialType.System_Object) {
                        return node.WithExpression(ApplyCast(node.Expression, parameterSymbol.Type));
                    }
                }
            }
        }

        return base.VisitArgument(node);
    }

    ExpressionSyntax ApplyCast(ExpressionSyntax expression, ITypeSymbol target) => ApplyCast(expression, target.ToTypeSyntax());

    ExpressionSyntax ApplyCast(ExpressionSyntax expression, TypeSyntax typeSyntax)
    {
        if (string.IsNullOrEmpty(typeSyntax.ToString())) {
            return expression;
        }

        expression = (ExpressionSyntax)Visit(expression);
        
        if (expression is InvocationExpressionSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or NameSyntax) {
            return CastExpression(typeSyntax, expression);
        }
        else {
            return CastExpression(typeSyntax, ParenthesizedExpression(expression));
        }
    }

}
