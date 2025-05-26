using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.Conversion;

public class ClassControlInfo(TypeSyntax type, IdentifierNameSyntax name)
{
    public IdentifierNameSyntax Name { get; internal set; } = name;

    public TypeSyntax Type { get; internal set; } = type;

    public IEnumerable<(NameSyntax name, ExpressionSyntax value)> Properties { get; set; } = [];

    public IEnumerable<ClassControlInfo> Children { get; set; } = [];

    public IdentifierNameSyntax GetIndexedName()
        => GetArrayIndex() is LiteralExpressionSyntax literal
            ? IdentifierName("_" + Name.Identifier.Text + "_" + literal.Token.Text)
            : Name;

    public LiteralExpressionSyntax GetArrayIndex()
        => Properties.FirstOrDefault(p => p.name is IdentifierNameSyntax id && id.Identifier.Text == "Index")
            .value as LiteralExpressionSyntax;

    public FieldDeclarationSyntax GetField() 
        => FieldDeclaration(
            default,
            Modifiers(isInternal: true),
            VariableDeclaration(
                Type, GetIndexedName().Identifier, 
                ImplicitObjectCreationExpression()
            )
        );

    public IEnumerable<ClassControlInfo> FlattenControls()
        => new[] { this }.Concat(Children.SelectMany(c => c.FlattenControls()));


    public IEnumerable<FieldDeclarationSyntax> GetFields() 
        => FlattenControls().Select(c => c.GetField());

    public IEnumerable<StatementSyntax> GetAssignments()
    {
        bool isFirst = true;
        foreach (var prop in Properties) {
            if (prop.name is IdentifierNameSyntax id && id.Identifier.Text == "Index") {
                continue;
            }

            NameSyntax name = GetIndexedName();
            foreach (var segment in prop.name.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
                name = QualifiedName(name, segment);
            }

            var stmt = ExpressionStatement(
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    name, prop.value));

            if (isFirst) {
                stmt = stmt.WithLeadingTrivia(TriviaList(Comment($"{Environment.NewLine}// {name}")));
                isFirst = false;
            }

            yield return stmt;
        }

        foreach (var child in Children) {
            foreach (var stmt in child.GetAssignments()) {
                yield return stmt;
            }
        }
    }

    public IEnumerable<(FieldDeclarationSyntax variable, StatementSyntax[] initializers)> GetArrays()
    {
        var arrayChildren = FlattenControls()
            .Where(c => c.GetArrayIndex() != null)
            .Select(c => new { Control = c, Index = (int)c.GetArrayIndex().Token.Value })
            .GroupBy(c => c.Control.Name.Identifier.Text, v => v,
                (k, v) => new { Name = k, Controls = v.ToDictionary(k => k.Index, v => v.Control) });

        bool isFirst = true;

        foreach (var array in arrayChildren) {
            var maxIndex = array.Controls.Max(c => c.Key);
            var first    = array.Controls.Values.First();

            var arrayType = ArrayType(first.Type)
                .WithRankSpecifiers(SingletonList(
                    ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                        OmittedArraySizeExpression())
                    )));

            var variable = FieldDeclaration(
                default, 
                Modifiers(isInternal: true),
                VariableDeclaration(arrayType, first.Name.Identifier, ArrayCreationExpression(
                    arrayType.WithRankSpecifiers(SingletonList(
                        ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(maxIndex + 1))
                        ))
                    ))
                ))
            );

            if (isFirst) {
                variable = variable.WithLeadingTrivia(TriviaList(Whitespace(Environment.NewLine)));
            }

            var initializers = array.Controls.OrderBy(c => c.Key).Select(c => ExpressionStatement(
                AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    ElementAccessExpression(
                        first.Name, BracketedArgumentList(SingletonSeparatedList(
                            Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(c.Key)))
                        ))
                    ),
                    c.Value.GetIndexedName()
                )
            )).ToArray();

            yield return (variable, initializers);
            isFirst = false;
        }
    }
}
