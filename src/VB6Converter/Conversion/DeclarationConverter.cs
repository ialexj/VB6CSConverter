using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class DeclarationConverter
{
    public static IEnumerable<VariableDeclarationSyntax> GetConstantDeclarations(ConstStmtContext @const, ConversionOptions options = null)
    {
        using var _ = new TraceMethod(@const);

        bool useDynamic = options?.UseDynamic ?? true;

        foreach (var sub in @const.constSubStmt()) {
            var initializer = GetValue(sub.valueStmt(), default);

            var type = sub.asTypeClause().ToTypeSyntax(true, useDynamic);

            bool isObjectLike = (type is PredefinedTypeSyntax pre && pre.Keyword.IsKind(SyntaxKind.ObjectKeyword))
                || (type is IdentifierNameSyntax id && id.Identifier.Text == "dynamic");
            if (isObjectLike) {
                if (initializer is LiteralExpressionSyntax literal) {
                    if (literal.IsKind(SyntaxKind.NumericLiteralExpression)) {
                        type = PredefinedType(Token(SyntaxKind.IntKeyword));
                    }
                    else if (literal.IsKind(SyntaxKind.StringLiteralExpression)) {
                        type = PredefinedType(Token(SyntaxKind.StringKeyword));
                    }
                }
            }

            yield return VariableDeclaration(type)
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(GetIdentifier(sub.ambiguousIdentifier()))
                            .WithInitializer(EqualsValueClause(initializer))
                    )
                );
        }
    }

    public static IEnumerable<VariableDeclarationSyntax> GetVariableDeclarations(VariableStmtContext var, bool defaultInit = false, ConversionOptions options = null)
    {
        using var _ = new TraceMethod(var);

        bool useDynamic = options?.UseDynamic ?? true;

        foreach (var sub in var.variableListStmt().variableSubStmt()) {

            var name = GetIdentifier(sub.ambiguousIdentifier());

            bool isArray = sub.LPAREN() is not null && sub.RPAREN() is not null;

            int arraySizeCounts = Math.Max(isArray ? 1 : 0, sub.subscripts()?.subscript().Length ?? 0);
            var omittedExpressions = Enumerable.Range(0, arraySizeCounts)
                .Select(_ => (SyntaxNodeOrToken)OmittedArraySizeExpression())
                .Intersperse(Token(SyntaxKind.CommaToken))
                .ToArray();

            var baseType = sub.asTypeClause().ToTypeSyntax(true, useDynamic);
            var type = isArray
                ? ArrayType(baseType, SingletonList(ArrayRankSpecifier(SeparatedList<ExpressionSyntax>(omittedExpressions))))
                : baseType;
            bool hasAsNew = sub.asTypeClause()?.NEW() is not null;

            var variable = VariableDeclarator(name);
            if (sub.subscripts() is SubscriptsContext subscripts) {
                variable = variable.WithInitializer(EqualsValueClause(GetArrayCreationExpression(baseType, subscripts, default)));
            }

            if (hasAsNew) {
                variable = variable.WithInitializer(
                    EqualsValueClause(ImplicitObjectCreationExpression()));
            }
            else if (defaultInit) {
                variable = variable.WithInitializer(
                    EqualsValueClause(
                        LiteralExpression(SyntaxKind.DefaultLiteralExpression, Token(SyntaxKind.DefaultKeyword))));
            }


            yield return VariableDeclaration(type).WithVariables(SingletonSeparatedList(variable));


        }
    }
}
