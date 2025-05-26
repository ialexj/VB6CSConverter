using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter;

internal static class RoslynHelpers
{
    static readonly string Version = DateTime.Now.ToString("O");

    public static CompilationUnitSyntax CompilationUnit(
        ClassDeclarationSyntax cls, NameSyntax ns = null) 
        => SyntaxFactory.CompilationUnit()
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    ns is not null
                        ? FileScopedNamespaceDeclaration(ns)
                            .WithMembers(SingletonList<MemberDeclarationSyntax>(cls))
                        : cls))
            .NormalizeWhitespace();

    public static ClassDeclarationSyntax WithGeneratedCodeAttribute(this ClassDeclarationSyntax classSyntax) 
        => classSyntax.WithAttributeLists(SingletonList(
            AttributeList(SingletonSeparatedList(
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
            ))
        ));

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

    public static VariableDeclarationSyntax VariableDeclaration(TypeSyntax type, SyntaxToken name, ExpressionSyntax initializer = null) 
        => SyntaxFactory.VariableDeclaration(type,
            SingletonSeparatedList(
                VariableDeclarator(name, null,
                    initializer != null ? EqualsValueClause(initializer) : null
                )
            )
        );

    public static NameSyntax ToName(this ExpressionSyntax expr)
    {
        if (expr is NameSyntax name) {
            return name;
        }
        else if (expr is MemberAccessExpressionSyntax member) {
            var obj = ToName(member.Expression);
            var target = member.Name;
            return QualifiedName(obj, target);
        }
        else {
            throw new ArgumentException("Expression is not a name");
        }
    }

    public static NameSyntax AppendName(this ExpressionSyntax left, ExpressionSyntax right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftName = left.ToName();
        var rightName = right.ToName();

        var current = leftName;
        foreach (var id in right.DescendantNodesAndSelf().OfType<SimpleNameSyntax>()) {
            current = QualifiedName(current, id);
        }

        return current;
    }

    public static IEnumerable<SimpleNameSyntax> EnumerateNames(this ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax inner) {
            yield return inner.Name;
            foreach (var expr in EnumerateNames(inner.Expression)) {
                yield return expr;
            }
        }
        else if (expression is ElementAccessExpressionSyntax element) {
            foreach (var expr in EnumerateNames(element.Expression)) {
                yield return expr;
            }
        }
        else if (expression is SimpleNameSyntax simple) {
            yield return simple;
        }
    }

    public static ArgumentListSyntax ArgumentList(params ExpressionSyntax[] args)
    {
        if (args is null || args.Length == 0) {
            return SyntaxFactory.ArgumentList();
        }
        else if (args.Length == 1) {
            return SyntaxFactory.ArgumentList(SingletonSeparatedList(Argument(args[0])));
        }
        else {
            return SyntaxFactory.ArgumentList(SeparatedList<ArgumentSyntax>(
                new SyntaxNodeOrTokenList(args
                    .Select(a => (SyntaxNodeOrToken)Argument(a))
                    .Intersperse(Token(SyntaxKind.CommaToken)))));
        }
    }

    public static ArgumentListSyntax ArgumentList(params ArgumentSyntax[] args)
    {
        if (args is null || args.Length == 0) {
            return SyntaxFactory.ArgumentList();
        }
        else if (args.Length == 1) {
            return SyntaxFactory.ArgumentList(SingletonSeparatedList(args[0]));
        }
        else {
            return SyntaxFactory.ArgumentList(SeparatedList<ArgumentSyntax>(
                [.. args
                    .Select(a => (SyntaxNodeOrToken)a)
                    .Intersperse(Token(SyntaxKind.CommaToken))]));
        }
    }

    public static StatementSyntax GetBlock(StatementSyntax[] statements, bool allowCollapse)
    {
        if (allowCollapse) {
            if (statements is null || statements.Length == 0) {
                return EmptyStatement();
            }
            else if (statements.Length == 1) {
                return statements[0];
            }
            else {
                return Block(statements);
            }
        }
        else {
            return Block(statements ?? []);
        }
    }

    public static TypeSyntax ToTypeSyntax(this TypeInfo typeInfo)
        => !string.IsNullOrEmpty(typeInfo.ConvertedType?.Name)
            ? ParseTypeName(typeInfo.ConvertedType.ToString())
            : PredefinedType(Token(SyntaxKind.ObjectKeyword));

    public static TypeSyntax ToTypeSyntax(this ITypeSymbol typeSymbol)
        => !string.IsNullOrEmpty(typeSymbol?.Name)
            ? ParseTypeName(typeSymbol.ToString())
            : PredefinedType(Token(SyntaxKind.ObjectKeyword));

    public static bool IsEquivalentSyntax(object oldValue, object newValue)
    {
        if (oldValue is null != newValue is null)
            return false;

        if (oldValue is SyntaxNode oldSn && newValue is SyntaxNode newSn) {
            oldSn = oldSn.NormalizeWhitespace();
            newSn = newSn.NormalizeWhitespace();
            return oldSn.IsEquivalentTo(newSn);
        }
        else if (oldValue is SyntaxToken oldToken && newValue is SyntaxToken newToken) {
            oldToken = oldToken.NormalizeWhitespace();
            newToken = newToken.NormalizeWhitespace();
            return oldToken.IsEquivalentTo(newToken);
        }
        else if (oldValue is IReadOnlyList<SyntaxNode> oldList && newValue is IReadOnlyList<SyntaxNode> newList) {
            if (oldList.Count != newList.Count) {
                return false;
            }
            for (int i = 0; i < oldList.Count; i++) {
                if (!IsEquivalentSyntax(oldList[i], newList[i])) {
                    return false;
                }
            }
            return true;
        }
        else if (oldValue is IReadOnlyList<SyntaxToken> oldTokenList && newValue is IReadOnlyList<SyntaxToken> newTokenList) {
            if (oldTokenList.Count != newTokenList.Count) {
                return false;
            }
            for (int i = 0; i < oldTokenList.Count; i++) {
                if (!IsEquivalentSyntax(oldTokenList[i], newTokenList[i])) {
                    return false;
                }
            }
            return true;
        }
        else {
            return string.Equals(oldValue.ToString(), newValue.ToString());
        }
    }

    public static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(this ITypeSymbol type)
    {
        var current = type;
        while (current != null) {
            yield return current;
            current = current.BaseType;
        }
    }

    public static T FirstDescendantOrSelf<T>(this SyntaxNode node) where T : SyntaxNode
    {
        return node.DescendantNodesAndSelf(i => i is not T).OfType<T>().FirstOrDefault();
    }
}