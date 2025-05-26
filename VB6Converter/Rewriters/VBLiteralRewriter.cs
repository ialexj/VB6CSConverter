using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class VBLiteralRewriter : LoggedRewriter
{
    static LiteralExpressionSyntax Lit(object value) => (value switch {
        string s => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(s)),
        char c => LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal(c)),
        int i => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i)),
        _ => throw new NotSupportedException()
    }).WithAdditionalAnnotations(new SyntaxAnnotation("Literal"));

    static LiteralExpressionSyntax Lit(object value, string comment)
        => Lit(value).WithTrailingTrivia(TriviaList(Comment($"/* {comment} */")));

    static readonly Dictionary<string, LiteralExpressionSyntax> _literals = new(StringComparer.InvariantCultureIgnoreCase) {
        ["vbNullString"] = Lit(string.Empty),
        ["vbNullChar"] = Lit('\0'),
        ["vbCr"] = Lit('\r'),
        ["vbLf"] = Lit('\n'),
        ["vbCrLf"] = Lit("\r\n"),
        ["vbFormFeed"] = Lit('\f'),
        ["vbBack"] = Lit('\b'),
        ["vbTab"] = Lit('\t'),

        ["vbBlack"] = Lit(0x0, "vbBlack"),
        ["vbRed"]   = Lit(0xFF, "vbRed"),
        ["vbGreen"] = Lit(0xFF00, "vbGreen"),
        ["vbYellow"] = Lit(0xFFFF, "vbYellow"),
        ["vbBlue"] = Lit(0xFF0000, "vbBlue"),
        ["vbMagenta"] = Lit(0xFF00FF, "vbMagenta"),
        ["vbCyan"] = Lit(0xFFFF00, "vbCyan"),
        ["vbWhite"] = Lit(0xFFFFFF, "vbWhite"),

        ["vbSunday"]    = Lit(1, "vbSunday"),
        ["vbMonday"]    = Lit(2, "vbMonday"),
        ["vbTuesday"]   = Lit(3, "vbTuesday"),
        ["vbWednesday"] = Lit(4, "vbWednesday"),
        ["vbThursday"]  = Lit(5, "vbThursday"),
        ["vbFriday"]    = Lit(6, "vbFriday"),
        ["vbSaturday"]  = Lit(7, "vbSaturday"),

    };

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (_literals.TryGetValue(node.Identifier.Text, out var literal)) {
                return literal;
            }

            return base.VisitIdentifierName(node);
        });

    public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        => Rewrite(node, node => {
            var result = (VariableDeclarationSyntax)base.VisitVariableDeclaration(node);

            var literals = result.GetAnnotatedNodes("Literal");
            if (literals.FirstOrDefault() is LiteralExpressionSyntax lit) {
                if (lit.IsKind(SyntaxKind.StringLiteralExpression)) {
                    return result.WithType(PredefinedType(Token(SyntaxKind.StringKeyword)));
                }
                else if (lit.IsKind(SyntaxKind.CharacterLiteralExpression)) {
                    return result.WithType(PredefinedType(Token(SyntaxKind.CharKeyword)));
                }
                else if (lit.IsKind(SyntaxKind.NumericLiteralExpression)) {
                    return result.WithType(PredefinedType(Token(SyntaxKind.IntKeyword)));
                }
            }

            return result;
        });
}
