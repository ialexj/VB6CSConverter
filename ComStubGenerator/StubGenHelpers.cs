using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ComStubGenerator;

/// <summary>
/// Roslyn helper utilities used by <see cref="ReferenceStubGenerator"/>.
/// Extracted subset of the VB6Converter.RoslynHelpers class.
/// </summary>
internal static class StubGenHelpers
{
    static readonly string Version = DateTime.Now.ToString("O");

    static AttributeListSyntax GeneratedCodeAttributeList()
        => AttributeList(SingletonSeparatedList(
            Attribute(ParseName("System.CodeDom.Compiler.GeneratedCode"), AttributeArgumentList(
                SeparatedList<AttributeArgumentSyntax>(
                    new SyntaxNodeOrToken[] {
                        AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("VB6Converter"))),
                        Token(SyntaxKind.CommaToken),
                        AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(Version)))
                    }
                )
            ))
            .WithLeadingTrivia(TriviaList(Whitespace(Environment.NewLine)))
        ));

    public static ClassDeclarationSyntax WithGeneratedCodeAttribute(this ClassDeclarationSyntax classSyntax)
        => classSyntax.WithAttributeLists(SingletonList(GeneratedCodeAttributeList()));

    public static EnumDeclarationSyntax WithGeneratedCodeAttribute(this EnumDeclarationSyntax enumSyntax)
        => enumSyntax.WithAttributeLists(SingletonList(GeneratedCodeAttributeList()));

    public static StructDeclarationSyntax WithGeneratedCodeAttribute(this StructDeclarationSyntax structSyntax)
        => structSyntax.WithAttributeLists(SingletonList(GeneratedCodeAttributeList()));

    public static InterfaceDeclarationSyntax WithGeneratedCodeAttribute(this InterfaceDeclarationSyntax interfaceSyntax)
        => interfaceSyntax.WithAttributeLists(SingletonList(GeneratedCodeAttributeList()));

    /// <summary>
    /// Builds <c>[System.Reflection.DefaultMember("<paramref name="memberName"/>")]</c>.
    /// Apply this to a type when its VB6 DISPID 0 member is a named property rather than
    /// an indexer (indexers already receive the attribute implicitly from the C# compiler).
    /// </summary>
    public static AttributeListSyntax DefaultMemberAttributeList(string memberName)
        => AttributeList(SingletonSeparatedList(
            Attribute(
                ParseName("System.Reflection.DefaultMember"),
                AttributeArgumentList(SingletonSeparatedList(
                    AttributeArgument(LiteralExpression(
                        SyntaxKind.StringLiteralExpression, Literal(memberName))))))
            .WithLeadingTrivia(TriviaList(Whitespace(Environment.NewLine)))));

    public static SyntaxTokenList Modifiers(
        bool isPublic = false, bool isInternal = false, bool isProtected = false,
        bool isStatic = false,
        bool isReadOnly = false, bool isPartial = false)
    {
        IEnumerable<SyntaxKind> GetKinds()
        {
            if (isPublic) yield return SyntaxKind.PublicKeyword;
            if (isInternal) yield return SyntaxKind.InternalKeyword;
            if (isProtected) yield return SyntaxKind.ProtectedKeyword;
            if (isStatic) yield return SyntaxKind.StaticKeyword;
            if (isReadOnly) yield return SyntaxKind.ReadOnlyKeyword;
            if (isPartial) yield return SyntaxKind.PartialKeyword;
        }

        return TokenList(GetKinds().Select(Token));
    }
}
