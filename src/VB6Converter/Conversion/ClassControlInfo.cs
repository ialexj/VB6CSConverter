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

    /// <summary>
    /// String-blob properties extracted from FRX resources (e.g. ListBox.List).
    /// Each entry carries the VB6 property name and the string items to add.
    /// </summary>
    public IReadOnlyList<(string PropertyName, string[] Items)> StringBlobProperties { get; set; } = [];

    public IEnumerable<ClassControlInfo> Children { get; set; } = [];

    public IdentifierNameSyntax GetIndexedName()
        => GetArrayIndex() is LiteralExpressionSyntax literal
            ? IdentifierName("_" + Name.Identifier.Text + "_" + literal.Token.Text)
            : Name;

    public LiteralExpressionSyntax? GetArrayIndex()
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

        foreach (var (propertyName, items) in StringBlobProperties) {
            if (items.Length == 0) continue;

            // Map known VB6 collection property names to WinForms equivalents
            if (string.Equals(propertyName, "List", StringComparison.OrdinalIgnoreCase)) {
                NameSyntax controlName = GetIndexedName();
                foreach (var item in items) {
                    yield return ExpressionStatement(
                        InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    controlName,
                                    IdentifierName("Items")),
                                IdentifierName("Add")),
                            ArgumentList(SingletonSeparatedList(
                                Argument(LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    Literal(item)))))));
                }
            }
            else {
                // Other string blob properties — emit a TODO comment
                yield return ExpressionStatement(
                    ParseExpression("default")
                        .WithLeadingTrivia(Comment($"// TODO: string blob property '{propertyName}' ({items.Length} items) — manual conversion required")));
            }
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
            .Select(c => new { Control = c, Index = (int)c.GetArrayIndex()!.Token!.Value })
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
