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
    /// <summary>
    /// Builds leading trivia for <c>/// &lt;DefaultMember&gt;<paramref name="memberName"/>&lt;/DefaultMember&gt;</c>,
    /// a custom XML doc-comment tag marking a type's VB6 DISPID-0 default member.
    /// Used instead of <c>[System.Reflection.DefaultMemberAttribute]</c> so the metadata:
    /// <list type="bullet">
    ///   <item>never conflicts with the attribute the C# compiler auto-adds to any type that
    ///     also has an indexer (e.g. a forwarding indexer for a nested default-member chain) —
    ///     a type cannot carry two instances of a non-<c>AllowMultiple</c> attribute;</item>
    ///   <item>needs no shared type dependency reachable from every default-member-bearing
    ///     class, so it works identically whether or not COM reference stub generation ran.</item>
    /// </list>
    /// Applied unconditionally to every type with a no-param DISPID 0 member, regardless of
    /// whether that type also has an indexer, so there is a single consistent mechanism for
    /// consumers (e.g. a future default-member-expansion rewriter) to look for.
    /// </summary>
    public static SyntaxTriviaList DefaultMemberDocComment(string memberName)
        => ParseLeadingTrivia($"/// <DefaultMember>{memberName}</DefaultMember>{Environment.NewLine}");

    /// <summary>
    /// Builds <c>[IndexedProperty("<paramref name="propertyName"/>")]</c>.
    /// Applied to getter and setter methods emitted from a parameterized COM property
    /// to distinguish them from regular methods with a similar naming pattern.
    /// </summary>
    public static AttributeListSyntax IndexedPropertyAttributeList(string propertyName)
        => AttributeList(SingletonSeparatedList(
            Attribute(
                IdentifierName("IndexedProperty"),
                AttributeArgumentList(SingletonSeparatedList(
                    AttributeArgument(LiteralExpression(
                        SyntaxKind.StringLiteralExpression, Literal(propertyName))))))));

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
