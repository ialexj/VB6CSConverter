using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class CommonConverter
{
    public static IdentifierNameSyntax GetIdentifierName(IIdentifierContext identifier, TypeHintContext typeHint = null)
    {
        return IdentifierName(GetIdentifier(identifier));
    }

    public static SyntaxToken GetIdentifier(IIdentifierContext identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        using var _ = new TraceMethod(identifier);

        var text = identifier.GetText();
        if (string.Equals(text, "Me", StringComparison.InvariantCultureIgnoreCase)) {
            text = "this";
        }
        else if (string.Equals(text, "vbNullString", StringComparison.InvariantCultureIgnoreCase)) {
            text = "\"\"";
        }

        return Identifier(text);
    }

    public static TypeSyntax ToTypeSyntax(this AsTypeClauseContext asType, bool objectIfNull = false, bool useDynamic = true)
    {
        if (asType == null) {
            return objectIfNull
                ? (useDynamic ? IdentifierName("dynamic") : PredefinedType(Token(SyntaxKind.ObjectKeyword)))
                : PredefinedType(Token(SyntaxKind.VoidKeyword));
        }

        var type = asType.type().ToTypeSyntax(useDynamic);

        if (asType.fieldLength() is FieldLengthContext length) {
            type = type.WithAdditionalAnnotations(new SyntaxAnnotation("FixedLength", length.INTEGERLITERAL().Symbol.Text));
        }

        return type;
    }

    public static TypeSyntax ToTypeSyntax(this TypeContext type, bool useDynamic = true)
    {
        static PredefinedTypeSyntax Predefined(SyntaxKind kind) => PredefinedType(Token(kind));

        if (type is null) {
            return Predefined(SyntaxKind.VoidKeyword);
        }

        TypeSyntax ts;
        if (type.complexType() is ComplexTypeContext complex) {
            if (complex.GetText() == "Currency") {
                return PredefinedType(Token(SyntaxKind.DecimalKeyword));
            }
            else if (complex.GetText() == "Any") {
                return PredefinedType(Token(SyntaxKind.ObjectKeyword));
            }
            else {
                return complex.ToTypeSyntax();
            }
        }
        else if (type.baseType() is BaseTypeContext baseType) {
            var typeSymbol = ((ITerminalNode)baseType.GetChild(0)).Symbol;
            TypeSyntax DynamicOrObject() => useDynamic ? IdentifierName("dynamic") : Predefined(SyntaxKind.ObjectKeyword);
            ts = typeSymbol.Type switch {
                BOOLEAN => Predefined(SyntaxKind.BoolKeyword),
                BYTE => Predefined(SyntaxKind.ByteKeyword),
                COLLECTION => ParseTypeName("Collection"),
                DATE => ParseTypeName("System.DateTime"),
                DOUBLE => Predefined(SyntaxKind.DoubleKeyword),
                INTEGER => Predefined(SyntaxKind.IntKeyword),
                LONG => Predefined(SyntaxKind.IntKeyword),
                SINGLE => Predefined(SyntaxKind.FloatKeyword),
                STRING => Predefined(SyntaxKind.StringKeyword),
                OBJECT => DynamicOrObject(),
                VARIANT => DynamicOrObject(),
                _ => ParseTypeName(typeSymbol.Text)
            };
        }
        else {
            ts = ParseTypeName(type.GetText())
                .WithError(TransformError.Create(type, "Not a complex or a base type"));
        }

        if (type.LPAREN() != null || type.RPAREN() != null) {
            ts = ArrayType(ts);
        }

        return ts;
    }

    public static TypeSyntax ToTypeSyntax(this ComplexTypeContext complex)
    {
        return ParseTypeName(complex.GetText());
    }

    public static ExpressionSyntax GetArrayCreationExpression(TypeSyntax elementType, SubscriptsContext subscripts, CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentNullException.ThrowIfNull(subscripts);

        var bounds = GetArrayBounds(subscripts, ctx);

        if (bounds.All(b => IsZeroLowerBound(b.LowerBound))) {
            return ArrayCreationExpression(BuildSizedArrayType(elementType, bounds.Select(b => GetZeroBasedArrayLengthExpression(b.UpperBound))));
        }

        if (bounds.Length == 1) {
            return ArrayCreationExpression(BuildSizedArrayType(elementType, [GetZeroBasedArrayLengthExpression(bounds[0].UpperBound)]))
                .WithError(TransformError.Create(subscripts, "Non-zero lower bound on single-dimensional array is not honored; indices are not offset"));
        }

        var lengths = GetIntArrayExpression(bounds.Select(b => GetLengthExpression(b.LowerBound, b.UpperBound)));
        var lowerBounds = GetIntArrayExpression(bounds.Select(b => b.LowerBound));
        var rectangularType = BuildRectangularArrayType(elementType, bounds.Length);
        var elementTypeOf = elementType is IdentifierNameSyntax { Identifier.Text: "dynamic" }
            ? PredefinedType(Token(SyntaxKind.ObjectKeyword))
            : elementType;

        return CastExpression(
            rectangularType,
            InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("System.Array"),
                    IdentifierName("CreateInstance")),
                ArgumentList(SeparatedList<ArgumentSyntax>([
                    Argument(TypeOfExpression(elementTypeOf)),
                    Token(SyntaxKind.CommaToken),
                    Argument(lengths),
                    Token(SyntaxKind.CommaToken),
                    Argument(lowerBounds)
                ]))));
    }

    public static ArrayBounds[] GetArrayBounds(SubscriptsContext subscripts, CallContext ctx)
    {
        ArgumentNullException.ThrowIfNull(subscripts);

        return subscripts.subscript().Select(subscript => {
            var values = subscript.valueStmt();
            var hasExplicitLowerBound = values.Length > 1;
            var lowerBound = hasExplicitLowerBound
                ? GetValue(values[0], ctx)
                : LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0));
            var upperBound = GetValue(hasExplicitLowerBound ? values[1] : values[0], ctx);

            return new ArrayBounds(subscript, lowerBound, upperBound);
        }).ToArray();
    }

    public static bool IsZeroLowerBound(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal
           && literal.IsKind(SyntaxKind.NumericLiteralExpression)
           && literal.Token.ValueText == "0";

    public static ExpressionSyntax GetZeroBasedArrayLengthExpression(ExpressionSyntax upperBound)
        => ParseExpression($"{upperBound} + 1");

    static ExpressionSyntax GetLengthExpression(ExpressionSyntax lowerBound, ExpressionSyntax upperBound)
        => ParseExpression($"({upperBound}) - ({lowerBound}) + 1");

    static ArrayTypeSyntax BuildSizedArrayType(TypeSyntax elementType, IEnumerable<ExpressionSyntax> sizes)
        => ArrayType(elementType, SingletonList(ArrayRankSpecifier(SeparatedList<ExpressionSyntax>(sizes))));

    static ArrayTypeSyntax BuildRectangularArrayType(TypeSyntax elementType, int rank)
    {
        var omittedSizes = Enumerable.Range(0, rank)
            .Select(_ => (SyntaxNodeOrToken)OmittedArraySizeExpression())
            .Intersperse(Token(SyntaxKind.CommaToken));

        return ArrayType(elementType, SingletonList(ArrayRankSpecifier(SeparatedList<ExpressionSyntax>(omittedSizes))));
    }

    static ExpressionSyntax GetIntArrayExpression(IEnumerable<ExpressionSyntax> expressions)
        => ArrayCreationExpression(
            ArrayType(
                PredefinedType(Token(SyntaxKind.IntKeyword)),
                SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
            InitializerExpression(SyntaxKind.ArrayInitializerExpression, SeparatedList<ExpressionSyntax>(expressions)));

    public static SyntaxToken GetVisibility(this IVisibilityContext v, SyntaxKind defaultVisibility = SyntaxKind.PrivateKeyword)
    {
        if (v is null) {
            return Token(defaultVisibility);
        }
        else if (v.PRIVATE() != null) {
            return Token(SyntaxKind.PrivateKeyword);
        }
        else if (v.PUBLIC() != null || v.GLOBAL() != null) {
            return Token(SyntaxKind.PublicKeyword);
        }
        else if (v.FRIEND() != null) {
            return Token(SyntaxKind.InternalKeyword);
        }
        else {
            return ParseToken(v.GetText())
                .WithError(TransformError.Create(v, "Unknown visibility"));
        }
    }

    public sealed record ArrayBounds(SubscriptContext Source, ExpressionSyntax LowerBound, ExpressionSyntax UpperBound);
}
