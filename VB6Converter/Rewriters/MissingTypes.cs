using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class MissingTypes
{
    public ConcurrentDictionary<string, (SyntaxToken name, NameSyntax ns, bool isType)> Identifiers = [];

    public ConcurrentDictionary<string, ConcurrentDictionary<string, PropertyDeclarationSyntax>> Properties = [];

    public ConcurrentDictionary<string, ConcurrentDictionary<string, MethodDeclarationSyntax>> Methods = [];

    public IEnumerable<CompilationUnitSyntax> GetCompilationUnits()
    {
        var statics = new SortedDictionary<string, FieldDeclarationSyntax>();

        foreach (var type in Identifiers) {
            var properties = Properties.TryGetValue(type.Key, out var p) ? p : [];
            var methods    = Methods.TryGetValue(type.Key, out var m) ? m : [];

            if (type.Value.isType || !properties.IsEmpty || !methods.IsEmpty) {
                var cu = GetIdentifierAsClass(type.Value.name, type.Value.ns);
                var cls = cu.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
                yield return cu.ReplaceNode(cls, cls
                    .AddMembers([.. properties.Values])
                    .AddMembers([.. methods.Values]))
                    .NormalizeWhitespace();
            }
            else {
                statics[type.Value.name.Text] = GetIdentifierAsField(type.Value.name, type.Value.ns);
            }
        }

        if (statics.Count > 0) {
            var cls = ClassDeclaration("_MissingMembers")
                .WithModifiers(TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.StaticKeyword)))
                .WithMembers(List(statics.Values.Cast<MemberDeclarationSyntax>().ToArray()));

            yield return CompilationUnit()
                .WithUsings(SingletonList(
                    UsingDirective(IdentifierName(cls.Identifier))
                        .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
                        .WithStaticKeyword(Token(SyntaxKind.StaticKeyword))
                ))
                .WithMembers(SingletonList<MemberDeclarationSyntax>(cls))
                .NormalizeWhitespace();
        }
    }

    static CompilationUnitSyntax GetIdentifierAsClass(SyntaxToken name, NameSyntax ns)
    {
        var cls = ClassDeclaration(name)
            .WithModifiers(TokenList(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.PartialKeyword)
            ));

        var cu = CompilationUnit();
        if (ns is not null) {
            return cu.WithMembers(
                SingletonList<MemberDeclarationSyntax>(FileScopedNamespaceDeclaration(ns)
                    .WithMembers(SingletonList<MemberDeclarationSyntax>(cls))));
        }
        else {
            return cu.WithMembers(SingletonList<MemberDeclarationSyntax>(cls));
        }
    }

    static FieldDeclarationSyntax GetIdentifierAsField(SyntaxToken name, NameSyntax ns)
    {
        return FieldDeclaration(
            [],
            TokenList(
                Token(SyntaxKind.PublicKeyword),
                Token(SyntaxKind.StaticKeyword),
                Token(SyntaxKind.ReadOnlyKeyword)
            ),
            VariableDeclaration(
                PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                SingletonSeparatedList(VariableDeclarator(name)
                    .WithInitializer(EqualsValueClause(
                        ImplicitObjectCreationExpression()
                    )
                ))
            )
        );
    }

    public void RegisterIdentifier(SyntaxToken name, NameSyntax ns, bool isType)
    {
        if (string.IsNullOrEmpty(name.Text)) return;
        var fullName = ns != null ? $"{ns}.{name.Text}" : name.Text;
        Identifiers.TryAdd(fullName.ToString(), (name, ns, isType));
    }

    public void RegisterProperty(string classType, SimpleNameSyntax name, TypeSyntax propertyType)
    {
        var prop = PropertyDeclaration(propertyType, name.Identifier)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(AccessorList(List([
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                ])));

        // Update the property type - if there are multiple types, then use object.
        Properties.GetOrAdd(classType, []).AddOrUpdate(name.Identifier.Text, prop, (key, existing) => {
            var exprop = existing;
            if (!exprop.Type.IsEquivalentTo(prop.Type)) {
                return exprop.WithType(PredefinedType(Token(SyntaxKind.ObjectKeyword)));
            }
            else {
                return exprop;
            }
        });
    }

    public void RegisterMethod(string type, SimpleNameSyntax memberName, IEnumerable<ParameterSyntax> parameters, TypeSyntax returnType)
    {
        var method = MethodDeclaration(returnType, memberName.Identifier)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(ParameterList(SeparatedList(parameters.ToArray())))
            .WithExpressionBody(ArrowExpressionClause(ThrowExpression(
                ObjectCreationExpression(
                    QualifiedName(IdentifierName(nameof(System)), IdentifierName(nameof(NotImplementedException))))
                    .WithArgumentList(ArgumentList())
                )))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        var signature = $"{method.Identifier}{method.ParameterList}"; // must include parameter list to allow overrides
        Methods.GetOrAdd(type, []).AddOrUpdate(signature, method, (key, existing) => {
            return existing; // todo
        });
    }
}
