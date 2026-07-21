using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
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

                if (TryGetEnumValueCastTarget(rightType.Type, leftType.Type, node.Right, out var enumCastType)) {
                    return node.WithRight(ApplyCast(node.Right, enumCastType));
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

            if (TryGetEnumValueCastTarget(rightType.Type, leftType.Type, node.Right, out var rightEnumCastType)) {
                return node.WithRight(ApplyCast(node.Right, rightEnumCastType));
            }

            if (TryGetEnumValueCastTarget(leftType.Type, rightType.Type, node.Left, out var leftEnumCastType)) {
                return node.WithLeft(ApplyCast(node.Left, leftEnumCastType));
            }

            return base.VisitBinaryExpression(node);
        });

    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.IsKind(SyntaxKind.LogicalNotExpression) || node.IsKind(SyntaxKind.LogicalOrExpression) || node.IsKind(SyntaxKind.LogicalAndExpression)) {
                var valueType = semantics.GetTypeInfo(node.Operand);
                if (valueType.Type?.SpecialType == SpecialType.System_Object) {
                    return node.WithOperand(ApplyCast(node.Operand, PredefinedType(Token(SyntaxKind.BoolKeyword))));
                }
            }
            return base.VisitPrefixUnaryExpression(node);
        });

    public override SyntaxNode VisitArgument(ArgumentSyntax node)
        => Rewrite(node, node => {
            if (node.DescendantNodes(n => n is not CastExpressionSyntax).OfType<CastExpressionSyntax>().Any()) {
                return base.VisitArgument(node);
            }

            // For arguments that are objects
            // that try to be inserted into parameters which are not objects
            // add a cast.

            if (node.Parent is ArgumentListSyntax list && list.Parent is InvocationExpressionSyntax invocation) {
                int index = list.Arguments.IndexOf(node);

                var argumentType = semantics.GetTypeInfo(node.Expression).Type;

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

                    if (parameterSymbol != null) {
                        if (argumentType is ITypeSymbol ts && ts.SpecialType == SpecialType.System_Object
                            && parameterSymbol.Type?.SpecialType != SpecialType.System_Object) {
                            return node.WithExpression(ApplyCast(node.Expression, parameterSymbol.Type));
                        }

                        if (TryGetEnumValueCastTarget(argumentType, parameterSymbol.Type, node.Expression, out var enumCastType)) {
                            return node.WithExpression(ApplyCast(node.Expression, enumCastType));
                        }
                    }
                }
            }

            return base.VisitArgument(node);
        });

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

    /// <summary>
    /// Determines whether <paramref name="sourceExpr"/> (of enum type <paramref name="sourceType"/>) can be
    /// safely cast to a different enum type <paramref name="targetType"/> because the source expression's
    /// resolved constant value matches the value of one of the target enum's members. Returns false when
    /// either type is not an enum, when the two types are the same enum, or when the source expression's
    /// value can't be resolved at compile time (e.g. a plain variable rather than a literal/enum member).
    /// </summary>
    bool TryGetEnumValueCastTarget(ITypeSymbol sourceType, ITypeSymbol targetType, ExpressionSyntax sourceExpr, out ITypeSymbol castType)
    {
        castType = null;

        if (sourceType?.TypeKind != TypeKind.Enum || targetType?.TypeKind != TypeKind.Enum) {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType)) {
            return false;
        }

        var constant = semantics.GetConstantValue(sourceExpr);
        if (!constant.HasValue || constant.Value is null) {
            return false;
        }

        long value;
        try {
            value = Convert.ToInt64(constant.Value);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException) {
            return false;
        }

        var hasMatch = targetType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.ConstantValue is not null)
            .Any(f => Convert.ToInt64(f.ConstantValue) == value);

        if (!hasMatch) {
            return false;
        }

        castType = targetType;
        return true;
    }

}
