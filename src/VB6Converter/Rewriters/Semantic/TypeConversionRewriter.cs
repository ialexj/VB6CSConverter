using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

public class TypeConversionRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node => {
            var targetType = semantics.GetTypeInfo(node.Left).Type;
            if (!ShouldConvert(node.Right, targetType, out var methodName)) {
                return base.VisitAssignmentExpression(node);
            }

            return node.WithRight(ApplyConvert(node.Right, methodName));
        });

    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        => Rewrite(node, node => {
            if (node.Initializer is null) {
                return base.VisitVariableDeclarator(node);
            }

            var targetType = semantics.GetDeclaredSymbol(node) switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                _ => null
            };

            if (!ShouldConvert(node.Initializer.Value, targetType, out var methodName)) {
                return base.VisitVariableDeclarator(node);
            }

            return node.WithInitializer(node.Initializer.WithValue(ApplyConvert(node.Initializer.Value, methodName)));
        });

    public override SyntaxNode VisitArgument(ArgumentSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is not ArgumentListSyntax list || list.Parent is not InvocationExpressionSyntax invocation) {
                return base.VisitArgument(node);
            }

            var parameterType = TryGetArgumentTargetType(node, list, invocation);
            if (!ShouldConvert(node.Expression, parameterType, out var methodName)) {
                return base.VisitArgument(node);
            }

            return node.WithExpression(ApplyConvert(node.Expression, methodName));
        });

    private ITypeSymbol TryGetArgumentTargetType(ArgumentSyntax argument, ArgumentListSyntax list, InvocationExpressionSyntax invocation)
    {
        var symbolInfo = semantics.GetSymbolInfo(invocation);
        var methods = symbolInfo.Symbol is IMethodSymbol resolved
            ? [resolved]
            : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();

        if (methods.Length == 0) {
            return null;
        }

        var candidateTypes = methods
            .Select(m => GetParameterTypeForArgument(m, argument, list))
            .Where(t => t is not null)
            .ToArray();

        if (candidateTypes.Length == 0) {
            return null;
        }

        var first = candidateTypes[0];
        return candidateTypes.All(t => SymbolEqualityComparer.Default.Equals(t, first))
            ? first
            : null;
    }

    private static ITypeSymbol GetParameterTypeForArgument(IMethodSymbol method, ArgumentSyntax argument, ArgumentListSyntax list)
    {
        if (argument.NameColon is not null) {
            var name = argument.NameColon.Name.Identifier.Text;
            return method.Parameters.FirstOrDefault(p => p.Name == name)?.Type;
        }

        int index = list.Arguments.IndexOf(argument);
        if (index < 0) {
            return null;
        }

        if (index < method.Parameters.Length) {
            return method.Parameters[index].Type;
        }

        return method.Parameters.LastOrDefault()?.IsParams == true
            ? method.Parameters[^1].Type
            : null;
    }

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node => {
            if (!IsComparisonOperator(node.Kind())) {
                return base.VisitBinaryExpression(node);
            }

            var leftType = semantics.GetTypeInfo(node.Left).Type;
            var rightType = semantics.GetTypeInfo(node.Right).Type;

            if (leftType?.SpecialType == SpecialType.System_String
                && rightType is not null
                && rightType.SpecialType != SpecialType.System_String) {
                if (ShouldConvert(node.Left, rightType, out var methodName)) {
                    return node.WithLeft(ApplyConvert(node.Left, methodName));
                }
            }
            else if (rightType?.SpecialType == SpecialType.System_String
                && leftType is not null
                && leftType.SpecialType != SpecialType.System_String) {
                if (ShouldConvert(node.Right, leftType, out var methodName)) {
                    return node.WithRight(ApplyConvert(node.Right, methodName));
                }
            }

            return base.VisitBinaryExpression(node);
        });

    private static bool IsComparisonOperator(SyntaxKind kind) => kind is
        SyntaxKind.EqualsExpression or
        SyntaxKind.NotEqualsExpression or
        SyntaxKind.LessThanExpression or
        SyntaxKind.LessThanOrEqualExpression or
        SyntaxKind.GreaterThanExpression or
        SyntaxKind.GreaterThanOrEqualExpression;

    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is null) {
                return base.VisitReturnStatement(node);
            }

            var enclosing = semantics.GetEnclosingSymbol(node.SpanStart);
            var returnType = enclosing switch
            {
                IMethodSymbol method => method.ReturnType,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (!ShouldConvert(node.Expression, returnType, out var methodName)) {
                return base.VisitReturnStatement(node);
            }

            return node.WithExpression(ApplyConvert(node.Expression, methodName));
        });

    private bool ShouldConvert(ExpressionSyntax expression, ITypeSymbol targetType, out string methodName)
    {
        methodName = null;

        if (targetType is null || HasExplicitCast(expression)) {
            return false;
        }

        methodName = GetConvertMethodName(targetType);
        if (methodName is null) {
            return false;
        }

        if (IsSystemConvertCall(expression, methodName)) {
            return false;
        }

        if (IsEnumLike(targetType)) {
            return false;
        }

        var sourceType = semantics.GetTypeInfo(expression).Type;
        if (sourceType is null || IsEnumLike(sourceType)) {
            return false;
        }

        var conversion = semantics.ClassifyConversion(expression, targetType);
        if (conversion.IsIdentity || conversion.IsImplicit) {
            return false;
        }

        return true;
    }

    private static bool HasExplicitCast(ExpressionSyntax expression)
        => expression.DescendantNodesAndSelf(n => n is not CastExpressionSyntax)
            .OfType<CastExpressionSyntax>()
            .Any();

    private bool IsSystemConvertCall(ExpressionSyntax expression, string methodName)
    {
        if (expression is not InvocationExpressionSyntax invocation) {
            return false;
        }

        var symbolInfo = semantics.GetSymbolInfo(invocation);

        if (symbolInfo.Symbol is IMethodSymbol resolved) {
            return IsMatchingConvertMethod(resolved, methodName);
        }

        return symbolInfo.CandidateSymbols
            .OfType<IMethodSymbol>()
            .Any(m => IsMatchingConvertMethod(m, methodName));
    }

    private static bool IsMatchingConvertMethod(IMethodSymbol method, string methodName)
        => method.Name == methodName
            && method.ContainingType?.ToDisplayString() == "System.Convert";

    private static bool IsEnumLike(ITypeSymbol type)
    {
        if (type is null) {
            return false;
        }

        var unwrapped = UnwrapNullable(type);
        return unwrapped?.TypeKind == TypeKind.Enum;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        => type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
                ? named.TypeArguments[0]
                : type;

    private static string GetConvertMethodName(ITypeSymbol targetType)
    {
        var unwrapped = UnwrapNullable(targetType);
        return unwrapped?.SpecialType switch
        {
            SpecialType.System_String => "ToString",
            SpecialType.System_Boolean => "ToBoolean",
            SpecialType.System_Char => "ToChar",
            SpecialType.System_Byte => "ToByte",
            SpecialType.System_SByte => "ToSByte",
            SpecialType.System_Int16 => "ToInt16",
            SpecialType.System_UInt16 => "ToUInt16",
            SpecialType.System_Int32 => "ToInt32",
            SpecialType.System_UInt32 => "ToUInt32",
            SpecialType.System_Int64 => "ToInt64",
            SpecialType.System_UInt64 => "ToUInt64",
            SpecialType.System_Single => "ToSingle",
            SpecialType.System_Double => "ToDouble",
            SpecialType.System_Decimal => "ToDecimal",
            SpecialType.System_DateTime => "ToDateTime",
            _ => null
        };
    }

    private ExpressionSyntax ApplyConvert(ExpressionSyntax expression, string methodName)
    {
        // Check source type before visiting — Visit may rewrite the expression.
        // VB6: True = -1, False = 0. When converting bool → integer, negate the
        // System.Convert result so that true maps to -1 (not 1).
        var isBoolToInt = semantics.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_Boolean;

        expression = (ExpressionSyntax)Visit(expression);

        if (isBoolToInt) {
            // Optimise literal cases: emit the integer constant directly.
            if (expression.IsKind(SyntaxKind.FalseLiteralExpression)) {
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))
                    .WithTriviaFrom(expression);
            }
            if (expression.IsKind(SyntaxKind.TrueLiteralExpression)) {
                return PrefixUnaryExpression(
                    SyntaxKind.UnaryMinusExpression,
                    LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1))
                        .WithTriviaFrom(expression));
            }

            // Non-literal bool expression: -(System.Convert.ToXxx(expr))
            var convertCall = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("System"),
                        IdentifierName("Convert")),
                    IdentifierName(methodName)),
                ArgumentList(SingletonSeparatedList(Argument(expression))));

            return PrefixUnaryExpression(
                SyntaxKind.UnaryMinusExpression,
                ParenthesizedExpression(convertCall));
        }

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("System"),
                    IdentifierName("Convert")),
                IdentifierName(methodName)),
            ArgumentList(SingletonSeparatedList(Argument(expression))));
    }
}
