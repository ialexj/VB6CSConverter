using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
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
            text = "string.Empty";
        }

        return Identifier(text);
    }

    public static TypeSyntax ToTypeSyntax(this AsTypeClauseContext asType, bool objectIfNull = false)
    {
        if (asType == null) {
            return objectIfNull 
                ? PredefinedType(Token(SyntaxKind.ObjectKeyword)) 
                : PredefinedType(Token(SyntaxKind.VoidKeyword));
        }
        
        var type = asType.type().ToTypeSyntax();
        
        if (asType.fieldLength() is FieldLengthContext length) {
            type = type.WithAdditionalAnnotations(new SyntaxAnnotation("FixedLength", length.INTEGERLITERAL().Symbol.Text));
        }

        return type;
    }

    public static TypeSyntax ToTypeSyntax(this TypeContext type)
    {
        static PredefinedTypeSyntax Predefined(SyntaxKind kind) => PredefinedType(Token(kind));

        if (type is null) {
            return Predefined(SyntaxKind.VoidKeyword);
        }

        TypeSyntax ts;
        if (type.complexType() is ComplexTypeContext complex) {
            return complex.ToTypeSyntax();
        }
        else if (type.baseType() is BaseTypeContext baseType) {
            var typeSymbol = ((ITerminalNode)baseType.GetChild(0)).Symbol;
            ts = typeSymbol.Type switch {
                BOOLEAN => Predefined(SyntaxKind.BoolKeyword),
                BYTE => Predefined(SyntaxKind.ByteKeyword),
                COLLECTION => ParseTypeName("List<object>").WithAdditionalAnnotations(new SyntaxAnnotation("Using", "System.Collections.Generic")),
                DATE => ParseTypeName("DateTime"),
                DOUBLE => Predefined(SyntaxKind.DoubleKeyword),
                INTEGER => Predefined(SyntaxKind.IntKeyword),
                LONG => Predefined(SyntaxKind.IntKeyword),
                SINGLE => Predefined(SyntaxKind.FloatKeyword),
                STRING => Predefined(SyntaxKind.StringKeyword),
                OBJECT => Predefined(SyntaxKind.ObjectKeyword),
                VARIANT => Predefined(SyntaxKind.ObjectKeyword),
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
}
