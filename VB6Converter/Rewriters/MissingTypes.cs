using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

#nullable enable

namespace VB6Converter.Rewriters;

public class MissingTypes
{
    [DebuggerDisplay("{FullName}")]
    public class TypeDef(SyntaxToken name, NameSyntax? ns)
    {
        public SyntaxToken Name { get; } = name;

        public NameSyntax? Namespace { get; } = ns;

        public string FullName => Namespace != null ? $"{Namespace}.{Name.Text}" : Name.Text;

        public ConcurrentDictionary<string, PropertyDeclarationSyntax> Properties = [];

        public ConcurrentDictionary<string, MethodDeclarationSyntax> Methods = [];

        public ConcurrentDictionary<string, FieldDeclarationSyntax> Constants = [];

        public CompilationUnitSyntax GetCompilationUnit() 
            => CompilationUnit(ClassDeclaration(Name)
                .WithModifiers(Modifiers(isPublic: true, isPartial: true))
                .WithMembers(List(
                    Enumerable.Empty<MemberDeclarationSyntax>()
                    .Concat(GetMembers(Constants))
                    .Concat(GetMembers(Properties))
                    .Concat(GetMembers(Methods))
                ))
                .WithGeneratedCodeAttribute(), Namespace);

        static IEnumerable<MemberDeclarationSyntax> GetMembers<T>(ConcurrentDictionary<string, T> dict) where T : MemberDeclarationSyntax
        {
            return dict.OrderBy(c => c.Key).Select(v => v.Value);
        }


        public void RegisterConstant(SyntaxToken name, TypeSyntax type)
        {
            if (string.IsNullOrEmpty(name.Text)) {
                return;
            }

            Constants.GetOrAdd(name.Text, f => FieldDeclaration(
                default,
                Modifiers(isPublic: true, isStatic: true, isReadOnly: true),
                VariableDeclaration(
                    type ?? PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                    name, LiteralExpression(
                        SyntaxKind.DefaultLiteralExpression,
                        Token(SyntaxKind.DefaultKeyword)))
            ));
        }

        public void AddProperty(SyntaxToken name, TypeSyntax propertyType, bool isStatic = false)
        {
            var prop = PropertyDeclaration(propertyType, name)
                .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                .WithAccessorList(AccessorList(List([
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                    ])));

            // Update the property type - if there are multiple types, then use object.
            Properties.AddOrUpdate(name.Text, prop, (key, existing) => {
                var exprop = existing;
                if (!exprop.Type.IsEquivalentTo(prop.Type)) {
                    return exprop.WithType(PredefinedType(Token(SyntaxKind.ObjectKeyword)));
                }
                else {
                    return exprop;
                }
            });
        }

        public void AddMethod(SyntaxToken memberName, ParameterListSyntax parameters, TypeSyntax returnType, bool isStatic = false)
        {
            var method = MethodDeclaration(returnType, memberName)
                .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                .WithParameterList(parameters)
                .WithExpressionBody(ArrowExpressionClause(ThrowExpression(
                    ObjectCreationExpression(
                        QualifiedName(IdentifierName(nameof(System)), IdentifierName(nameof(NotImplementedException))))
                        .WithArgumentList(ArgumentList())
                    )))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            var signature = $"{method.Identifier}{method.ParameterList}"; // must include parameter list to allow overrides
            Methods.AddOrUpdate(signature, method, (key, existing) => {
                return existing; // todo
            });
        }
    }

    public ConcurrentDictionary<string, TypeDef> Types = [];


    public ConcurrentDictionary<string, (SyntaxToken name, NameSyntax ns, bool isType)> Identifiers = [];

    public ConcurrentDictionary<string, ConcurrentDictionary<string, PropertyDeclarationSyntax>> Properties = [];

    public ConcurrentDictionary<string, ConcurrentDictionary<string, MethodDeclarationSyntax>> Methods = [];

    public ConcurrentDictionary<string, ConcurrentDictionary<string, FieldDeclarationSyntax>> Fields = [];

    public IEnumerable<CompilationUnitSyntax> GetCompilationUnits()
    {
        foreach (var type in Types) {
            yield return type.Value.GetCompilationUnit();
        }
    }

    public TypeDef RegisterType(string classType, NameSyntax? ns = null)
    {
        return Types.GetOrAdd(classType, t => new TypeDef(Identifier(t), ns));
    }

    public void RegisterProperty(TypeDef classType, SyntaxToken name, TypeSyntax propertyType, bool isStatic = false)
    {
        var prop = PropertyDeclaration(propertyType, name)
            .WithModifiers(TokenList(GetModifiers(isStatic)))
            .WithAccessorList(AccessorList(List([
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                ])));

        // Update the property type - if there are multiple types, then use object.
        classType.Properties.AddOrUpdate(name.Text, prop, (key, existing) => {
            var exprop = existing;
            if (!exprop.Type.IsEquivalentTo(prop.Type)) {
                return exprop.WithType(PredefinedType(Token(SyntaxKind.ObjectKeyword)));
            }
            else {
                return exprop;
            }
        });
    }

    public void RegisterMethod(TypeDef typeName, SyntaxToken memberName, ParameterListSyntax parameters, TypeSyntax returnType, bool isStatic = false)
    {
        var method = MethodDeclaration(returnType, memberName)
            .WithModifiers(TokenList(GetModifiers(isStatic)))
            .WithParameterList(parameters)
            .WithExpressionBody(ArrowExpressionClause(ThrowExpression(
                ObjectCreationExpression(
                    QualifiedName(IdentifierName(nameof(System)), IdentifierName(nameof(NotImplementedException))))
                    .WithArgumentList(ArgumentList())
                )))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        var signature = $"{method.Identifier}{method.ParameterList}"; // must include parameter list to allow overrides
        typeName.Methods.AddOrUpdate(signature, method, (key, existing) => {
            return existing; // todo
        });
    }

    static IEnumerable<SyntaxToken> GetModifiers(bool isStatic)
    {
        yield return Token(SyntaxKind.PublicKeyword);
        if (isStatic) {
            yield return Token(SyntaxKind.StaticKeyword);
        }
    }
}
