using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Rewrites numeric literals in assignments where the LHS has a concrete type that
/// requires an explicit literal form:
/// <list type="bullet">
/// <item><c>bool</c>  — integer <c>0</c> → <c>false</c>, non-zero integer → <c>true</c></item>
/// <item><c>decimal</c> — bare double/int literal → suffix with <c>M</c> (e.g. <c>8.25</c> → <c>8.25M</c>)</item>
/// <item><c>float</c>  — bare double/int literal → suffix with <c>F</c> (e.g. <c>8.25</c> → <c>8.25F</c>)</item>
/// </list>
/// VB6's ValueConverter always emits floating-point values as bare C# <c>double</c> literals,
/// causing compile errors for <c>decimal</c>/<c>float</c> properties. VB6 also uses <c>-1</c>/<c>0</c>
/// for <c>True</c>/<c>False</c>, which are not valid C# <c>bool</c> assignments.
/// </summary>
public class LiteralCoercionRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node =>
        {
            var lhsType = semantics.GetTypeInfo(node.Left).Type;
            if (lhsType is null)
                return base.VisitAssignmentExpression(node);

            var newRight = CoerceByType(node.Right, lhsType);
            if (!ReferenceEquals(newRight, node.Right))
                return node.WithRight(newRight);

            return base.VisitAssignmentExpression(node);
        });

    public override SyntaxNode VisitParameter(ParameterSyntax node)
        => Rewrite(node, node =>
        {
            if (node.Default is null)
                return base.VisitParameter(node);

            var symbol = semantics.GetDeclaredSymbol(node);
            if (symbol is null)
                return base.VisitParameter(node);

            var coerced = CoerceByType(node.Default.Value, symbol.Type);
            if (!ReferenceEquals(coerced, node.Default.Value))
                return node.WithDefault(node.Default.WithValue(coerced));

            return base.VisitParameter(node);
        });

    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        => Rewrite(node, node =>
        {
            if (node.Initializer is null)
                return base.VisitVariableDeclarator(node);

            var symbol = semantics.GetDeclaredSymbol(node);
            ITypeSymbol? varType = symbol switch
            {
                ILocalSymbol local   => local.Type,
                IFieldSymbol field   => field.Type,
                _ => null
            };
            if (varType is null)
                return base.VisitVariableDeclarator(node);

            var coerced = CoerceByType(node.Initializer.Value, varType);
            if (!ReferenceEquals(coerced, node.Initializer.Value))
                return node.WithInitializer(node.Initializer.WithValue(coerced));

            return base.VisitVariableDeclarator(node);
        });

    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        => Rewrite(node, node =>
        {
            if (node.Expression is null)
                return base.VisitReturnStatement(node);

            var enclosing = semantics.GetEnclosingSymbol(node.SpanStart);
            ITypeSymbol? returnType = enclosing switch
            {
                IMethodSymbol   m => m.ReturnType,
                IPropertySymbol p => p.Type,
                _ => null
            };
            if (returnType is null)
                return base.VisitReturnStatement(node);

            var coerced = CoerceByType(node.Expression, returnType);
            if (!ReferenceEquals(coerced, node.Expression))
                return node.WithExpression(coerced);

            return base.VisitReturnStatement(node);
        });

    public override SyntaxNode VisitArgument(ArgumentSyntax node)
        => Rewrite(node, node =>
        {
            if (node.Parent is not ArgumentListSyntax list
                || list.Parent is not InvocationExpressionSyntax invocation)
                return base.VisitArgument(node);

            var symbolInfo = semantics.GetSymbolInfo(invocation);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault())
                    is not IMethodSymbol method)
                return base.VisitArgument(node);

            IParameterSymbol? paramSymbol;
            if (node.NameColon is not null)
            {
                var name = node.NameColon.Name.Identifier.Text;
                paramSymbol = method.Parameters.FirstOrDefault(p => p.Name == name);
            }
            else
            {
                int index = list.Arguments.IndexOf(node);
                paramSymbol = index >= 0 && index < method.Parameters.Length
                    ? method.Parameters[index]
                    : null;
            }

            if (paramSymbol is null)
                return base.VisitArgument(node);

            var coerced = CoerceByType(node.Expression, paramSymbol.Type);
            if (!ReferenceEquals(coerced, node.Expression))
                return node.WithExpression(coerced);

            return base.VisitArgument(node);
        });

    private ExpressionSyntax CoerceByType(ExpressionSyntax expr, ITypeSymbol targetType)
    {
        if (targetType.TypeKind == TypeKind.Enum && targetType is INamedTypeSymbol enumType)
        {
            var coerced = CoerceEnumToEnum(expr, enumType);
            if (!ReferenceEquals(coerced, expr))
                return coerced;
            return CoerceToEnumMember(expr, enumType);
        }

        return targetType.SpecialType switch
        {
            SpecialType.System_Boolean => CoerceToBool(expr),
            SpecialType.System_Decimal => CoerceNumericLiteral(expr,
                v => v is double or int,
                text => Literal(decimal.Parse(text))),
            SpecialType.System_Single  => CoerceNumericLiteral(expr,
                v => v is double or int,
                text => Literal(float.Parse(text))),
            SpecialType.System_Int32   => CoerceUIntToInt(expr),
            SpecialType.System_UInt32  => CoerceIntToUInt(expr),
            _ => expr
        };
    }

    private static ExpressionSyntax CoerceToBool(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit
            && lit.IsKind(SyntaxKind.NumericLiteralExpression)
            && lit.Token.Value is int iv)
        {
            var kwKind   = iv == 0 ? SyntaxKind.FalseKeyword           : SyntaxKind.TrueKeyword;
            var exprKind = iv == 0 ? SyntaxKind.FalseLiteralExpression  : SyntaxKind.TrueLiteralExpression;
            return LiteralExpression(exprKind,
                Token(lit.Token.LeadingTrivia, kwKind, lit.Token.TrailingTrivia));
        }

        // VB6 True = -1: unary minus wrapping a non-zero integer literal → true
        if (expr is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.UnaryMinusExpression)
            && unary.Operand is LiteralExpressionSyntax inner
            && inner.IsKind(SyntaxKind.NumericLiteralExpression)
            && inner.Token.Value is int innerVal && innerVal != 0)
        {
            return LiteralExpression(SyntaxKind.TrueLiteralExpression,
                Token(unary.OperatorToken.LeadingTrivia, SyntaxKind.TrueKeyword, inner.Token.TrailingTrivia));
        }

        return expr;
    }

    /// <summary>
    /// When the RHS is a <c>uint</c> numeric literal (e.g. a hex literal like <c>0x80000010</c>
    /// that overflows <c>int</c>) and the LHS is <c>int</c>, rewrite the literal to its
    /// signed-integer equivalent.  For all values except <c>int.MinValue</c> this produces a
    /// unary-minus expression wrapping the absolute value (e.g. <c>-2147483632</c>).  For the
    /// single special case of <c>0x80000000</c> it emits the identifier <c>int.MinValue</c>.
    /// </summary>
    private static ExpressionSyntax CoerceUIntToInt(ExpressionSyntax expr)
    {
        if (expr is not LiteralExpressionSyntax lit
            || !lit.IsKind(SyntaxKind.NumericLiteralExpression)
            || lit.Token.Value is not uint uintVal)
            return expr;

        var intVal = unchecked((int)uintVal);

        if (intVal == int.MinValue)
        {
            // -(2147483648) would make the inner literal uint, giving a long result.
            // Emit int.MinValue instead.
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(lit.Token.LeadingTrivia, SyntaxKind.IntKeyword, TriviaList())),
                IdentifierName(Identifier(TriviaList(), "MinValue", lit.Token.TrailingTrivia)));
        }

        // intVal is always negative here: uintVal > int.MaxValue implies intVal < 0.
        // -intVal fits in int because intVal != int.MinValue.
        var absLit = LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            Literal(-intVal).WithTrailingTrivia(lit.Token.TrailingTrivia));

        return PrefixUnaryExpression(
            SyntaxKind.UnaryMinusExpression,
            Token(lit.Token.LeadingTrivia, SyntaxKind.MinusToken, TriviaList()),
            absLit);
    }

    /// <summary>
    /// Rewrites a negative int literal (or unary-minus wrapping a positive literal) to its
    /// unsigned 32-bit equivalent when the LHS is <c>uint</c>.  For example <c>-2147483632</c>
    /// becomes <c>2147483664</c> and <c>-1</c> becomes <c>4294967295</c>.
    /// </summary>
    private static ExpressionSyntax CoerceIntToUInt(ExpressionSyntax expr)
    {
        if (expr is not PrefixUnaryExpressionSyntax unary
            || !unary.IsKind(SyntaxKind.UnaryMinusExpression)
            || unary.Operand is not LiteralExpressionSyntax inner
            || !inner.IsKind(SyntaxKind.NumericLiteralExpression))
            return expr;

        uint uintVal;
        if (inner.Token.Value is int posInt && posInt > 0)
            uintVal = unchecked((uint)(-posInt));
        else if (inner.Token.Value is uint posUint)
            // Handles -(2147483648) where the inner literal is already uint.
            uintVal = unchecked((uint)(-(long)posUint));
        else
            return expr;

        var newToken = Literal(uintVal.ToString(), uintVal)
            .WithLeadingTrivia(unary.OperatorToken.LeadingTrivia)
            .WithTrailingTrivia(inner.Token.TrailingTrivia);
        return LiteralExpression(SyntaxKind.NumericLiteralExpression, newToken);
    }

    /// <summary>
    /// Replaces a numeric literal (or unary-minus wrapping one) when <paramref name="shouldCoerce"/>
    /// is true for the token's value. Preserves the original token's leading/trailing trivia.
    /// Uses the token <em>text</em> (not the boxed value) to avoid floating-point precision loss
    /// when parsing to <c>decimal</c> or <c>float</c>.
    /// </summary>
    private static ExpressionSyntax CoerceNumericLiteral(
        ExpressionSyntax expr,
        Func<object, bool> shouldCoerce,
        Func<string, SyntaxToken> makeToken)
    {
        if (expr is LiteralExpressionSyntax lit
            && lit.IsKind(SyntaxKind.NumericLiteralExpression)
            && lit.Token.Value is object tokenVal
            && shouldCoerce(tokenVal))
        {
            var newToken = makeToken(lit.Token.Text)
                .WithLeadingTrivia(lit.Token.LeadingTrivia)
                .WithTrailingTrivia(lit.Token.TrailingTrivia);
            return lit.WithToken(newToken);
        }

        if (expr is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            var newOperand = CoerceNumericLiteral(unary.Operand, shouldCoerce, makeToken);
            if (!ReferenceEquals(newOperand, unary.Operand))
                return unary.WithOperand((ExpressionSyntax)newOperand);
        }

        return expr;
    }

    /// <summary>
    /// Attempts to extract a numeric integer value from a literal expression (positive or negative).
    /// Returns <see langword="false"/> for non-numeric or non-literal shapes.
    /// </summary>
    private static bool TryGetNumericValue(
        ExpressionSyntax expr,
        out long value,
        out SyntaxTriviaList leadingTrivia,
        out SyntaxTriviaList trailingTrivia)
    {
        if (expr is LiteralExpressionSyntax lit
            && lit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            leadingTrivia  = lit.Token.LeadingTrivia;
            trailingTrivia = lit.Token.TrailingTrivia;
            value = lit.Token.Value switch
            {
                int    i => i,
                uint   u => (long)u,
                long   l => l,
                ulong  ul => (long)ul,
                _        => 0
            };
            return lit.Token.Value is int or uint or long or ulong;
        }

        if (expr is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.UnaryMinusExpression)
            && unary.Operand is LiteralExpressionSyntax innerLit
            && innerLit.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            leadingTrivia  = unary.OperatorToken.LeadingTrivia;
            trailingTrivia = innerLit.Token.TrailingTrivia;
            var raw = innerLit.Token.Value switch
            {
                int    i => (long)i,
                uint   u => (long)u,
                long   l => l,
                ulong  ul => (long)ul,
                _        => 0L
            };
            value = -raw;
            return innerLit.Token.Value is int or uint or long or ulong;
        }

        value          = 0;
        leadingTrivia  = TriviaList();
        trailingTrivia = TriviaList();
        return false;
    }

    /// <summary>
    /// Rewrites a member-access expression whose type is a different enum than <paramref name="targetType"/>
    /// to use the target enum's member of the same name (case-insensitive, per VB6 rules).
    /// The emitted member name uses the target enum's canonical casing.
    /// Returns <paramref name="expr"/> unchanged when no case-insensitive name match exists,
    /// the source is already <paramref name="targetType"/>, or the expression is not a member access.
    /// </summary>
    private ExpressionSyntax CoerceEnumToEnum(ExpressionSyntax expr, INamedTypeSymbol targetType)
    {
        if (expr is not MemberAccessExpressionSyntax memberAccess)
            return expr;

        var sourceType = semantics.GetTypeInfo(expr).Type;
        if (sourceType is null || sourceType.TypeKind != TypeKind.Enum)
            return expr;

        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            return expr;

        var sourceMemberName = memberAccess.Name.Identifier.Text;
        var targetMember = targetType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => string.Equals(f.Name, sourceMemberName, StringComparison.OrdinalIgnoreCase));

        if (targetMember is null)
            return expr;

        var leading  = expr.GetFirstToken().LeadingTrivia;
        var trailing = expr.GetLastToken().TrailingTrivia;

        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName(Identifier(leading, targetType.Name, TriviaList())),
            IdentifierName(Identifier(TriviaList(), targetMember.Name, trailing)));
    }

    /// <summary>
    /// Rewrites a numeric literal assigned to an enum-typed LHS.
    /// If a member of <paramref name="enumType"/> has the same value as the literal, emits
    /// <c>EnumType.MemberName</c>. Otherwise emits a cast <c>(EnumType)value</c>.
    /// Returns <paramref name="expr"/> unchanged when the expression is not a numeric literal.
    /// </summary>
    private static ExpressionSyntax CoerceToEnumMember(ExpressionSyntax expr, INamedTypeSymbol enumType)
    {
        if (!TryGetNumericValue(expr, out var value, out var leading, out var trailing))
            return expr;

        var match = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.ConstantValue is not null)
            .FirstOrDefault(f => Convert.ToInt64(f.ConstantValue) == value);

        NameSyntax enumNameWithNamespace = enumType.ContainingNamespace.IsGlobalNamespace
            ? IdentifierName(enumType.Name)
            : QualifiedName(
                IdentifierName(Identifier(leading, enumType.ContainingNamespace.ToString(), TriviaList())),
                IdentifierName(Identifier(enumType.Name))
            );

        if (match is not null) {
            // EnumType.MemberName  — preserve original leading/trailing trivia
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                enumType.ToNameSyntax(),
                IdentifierName(Identifier(TriviaList(), match.Name, trailing)));
        }

        // No matching member — emit (EnumType)value
        // Put the original leading trivia on the cast's open-paren token.
        var innerExpr = expr.WithoutLeadingTrivia();
        return CastExpression(
            Token(leading, SyntaxKind.OpenParenToken, TriviaList()),
            enumType.ToNameSyntax(),
            Token(SyntaxKind.CloseParenToken),
            innerExpr.WithTrailingTrivia(trailing));
    }
}
