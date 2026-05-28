using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

public class EnumToNumberCastRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node => {
            if (HasExplicitCast(node.Right)) {
                return base.VisitAssignmentExpression(node);
            }

            var leftType = semantics.GetTypeInfo(node.Left).Type;
            var rightType = semantics.GetTypeInfo(node.Right).Type;

            if (TryGetEnumToNumericTarget(rightType, leftType, out var targetType)) {
                return node.WithRight(ApplyCast(node.Right, targetType));
            }

            return base.VisitAssignmentExpression(node);
        });

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node => {
            var leftType = semantics.GetTypeInfo(node.Left).Type;
            var rightType = semantics.GetTypeInfo(node.Right).Type;

            if (TryGetEnumToNumericTarget(leftType, rightType, out var leftTargetType) && !HasExplicitCast(node.Left)) {
                return node.WithLeft(ApplyCast(node.Left, leftTargetType));
            }

            if (TryGetEnumToNumericTarget(rightType, leftType, out var rightTargetType) && !HasExplicitCast(node.Right)) {
                return node.WithRight(ApplyCast(node.Right, rightTargetType));
            }

            return base.VisitBinaryExpression(node);
        });

    public override SyntaxNode VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
        => Rewrite(node, node => {
            if (HasExplicitCast(node.Value)) {
                return base.VisitCaseSwitchLabel(node);
            }

            var switchStatement = node.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault();
            if (switchStatement is null) {
                return base.VisitCaseSwitchLabel(node);
            }

            var switchType = semantics.GetTypeInfo(switchStatement.Expression).Type;
            var caseType = semantics.GetTypeInfo(node.Value).Type;
            if (TryGetEnumToNumericTarget(caseType, switchType, out var targetType)) {
                return node.WithValue(ApplyCast(node.Value, targetType));
            }

            return base.VisitCaseSwitchLabel(node);
        });

    public override SyntaxNode VisitArgument(ArgumentSyntax node)
        => Rewrite(node, node => {
            if (HasExplicitCast(node.Expression)) {
                return base.VisitArgument(node);
            }

            if (node.Parent is not ArgumentListSyntax list || list.Parent is not InvocationExpressionSyntax invocation) {
                return base.VisitArgument(node);
            }

            var symbolInfo = semantics.GetSymbolInfo(invocation);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method) {
                return base.VisitArgument(node);
            }

            IParameterSymbol parameter;
            if (node.NameColon is not null) {
                var name = node.NameColon.Name.Identifier.Text;
                parameter = method.Parameters.FirstOrDefault(p => p.Name == name);
            }
            else {
                int index = list.Arguments.IndexOf(node);
                parameter = index >= 0 && index < method.Parameters.Length
                    ? method.Parameters[index]
                    : null;
            }

            if (parameter is null) {
                return base.VisitArgument(node);
            }

            var argumentType = semantics.GetTypeInfo(node.Expression).Type;
            if (TryGetEnumToNumericTarget(argumentType, parameter.Type, out var targetType)) {
                return node.WithExpression(ApplyCast(node.Expression, targetType));
            }

            return base.VisitArgument(node);
        });

    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is null || HasExplicitCast(node.Expression)) {
                return base.VisitReturnStatement(node);
            }

            var enclosing = semantics.GetEnclosingSymbol(node.SpanStart);
            ITypeSymbol returnType = enclosing switch
            {
                IMethodSymbol method => method.ReturnType,
                IPropertySymbol property => property.Type,
                _ => null
            };

            var valueType = semantics.GetTypeInfo(node.Expression).Type;
            if (TryGetEnumToNumericTarget(valueType, returnType, out var targetType)) {
                return node.WithExpression(ApplyCast(node.Expression, targetType));
            }

            return base.VisitReturnStatement(node);
        });

    public override SyntaxNode VisitEqualsValueClause(EqualsValueClauseSyntax node)
        => Rewrite(node, node => {
            if (HasExplicitCast(node.Value)) {
                return base.VisitEqualsValueClause(node);
            }

            var targetType = GetEqualsValueTargetType(node);
            var valueType = semantics.GetTypeInfo(node.Value).Type;
            if (TryGetEnumToNumericTarget(valueType, targetType, out var castType)) {
                return node.WithValue(ApplyCast(node.Value, castType));
            }

            return base.VisitEqualsValueClause(node);
        });

    private static bool HasExplicitCast(ExpressionSyntax expression)
        => expression.DescendantNodesAndSelf(n => n is not CastExpressionSyntax)
            .OfType<CastExpressionSyntax>()
            .Any();

    private ITypeSymbol GetEqualsValueTargetType(EqualsValueClauseSyntax node)
        => node.Parent switch
        {
            VariableDeclaratorSyntax declarator => semantics.GetDeclaredSymbol(declarator) switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                _ => null
            },
            ParameterSyntax parameter => (semantics.GetDeclaredSymbol(parameter) as IParameterSymbol)?.Type,
            PropertyDeclarationSyntax property => (semantics.GetDeclaredSymbol(property) as IPropertySymbol)?.Type,
            _ => null
        };

    private static bool TryGetEnumToNumericTarget(ITypeSymbol sourceType, ITypeSymbol targetType, out ITypeSymbol castType)
    {
        castType = null;

        if (!IsEnumLike(sourceType)) {
            return false;
        }

        if (!IsNumericLike(targetType)) {
            return false;
        }

        castType = targetType;
        return true;
    }

    private static bool IsEnumLike(ITypeSymbol type)
    {
        if (type is null) {
            return false;
        }

        var unwrapped = UnwrapNullable(type);
        return unwrapped?.TypeKind == TypeKind.Enum;
    }

    private static bool IsNumericLike(ITypeSymbol type)
    {
        if (type is null) {
            return false;
        }

        var unwrapped = UnwrapNullable(type);
        return unwrapped?.SpecialType is
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

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        => type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
                ? named.TypeArguments[0]
                : type;

    private ExpressionSyntax ApplyCast(ExpressionSyntax expression, ITypeSymbol targetType)
        => ApplyCast(expression, targetType.ToTypeSyntax());

    private ExpressionSyntax ApplyCast(ExpressionSyntax expression, TypeSyntax targetType)
    {
        expression = (ExpressionSyntax)Visit(expression);

        if (expression is InvocationExpressionSyntax
            or MemberAccessExpressionSyntax
            or ElementAccessExpressionSyntax
            or NameSyntax)
        {
            return CastExpression(targetType, expression);
        }

        return CastExpression(targetType, ParenthesizedExpression(expression));
    }
}
