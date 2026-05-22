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

    static MemberAccessExpressionSyntax EnumMember(string typeName, string memberName)
        => MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(typeName),
                IdentifierName(memberName))
            .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "Microsoft.VisualBasic"));

    static readonly Dictionary<string, ExpressionSyntax> _literals = new(StringComparer.InvariantCultureIgnoreCase) {
        // String / char constants
        ["vbNullString"] = Lit(string.Empty),
        ["vbNullChar"]   = Lit('\0'),
        ["vbCr"]         = Lit('\r'),
        ["vbLf"]         = Lit('\n'),
        ["vbCrLf"]       = Lit("\r\n"),
        ["vbFormFeed"]   = Lit('\f'),
        ["vbBack"]       = Lit('\b'),
        ["vbTab"]        = Lit('\t'),

        // Color constants (OLE COLORREF BGR integers — no typed .NET equivalent)
        ["vbBlack"]   = Lit(0x000000, "vbBlack"),
        ["vbRed"]     = Lit(0x0000FF, "vbRed"),
        ["vbGreen"]   = Lit(0x00FF00, "vbGreen"),
        ["vbYellow"]  = Lit(0x00FFFF, "vbYellow"),
        ["vbBlue"]    = Lit(0xFF0000, "vbBlue"),
        ["vbMagenta"] = Lit(0xFF00FF, "vbMagenta"),
        ["vbCyan"]    = Lit(0xFFFF00, "vbCyan"),
        ["vbWhite"]   = Lit(0xFFFFFF, "vbWhite"),

        // Day of week — FirstDayOfWeek enum
        ["vbSunday"]    = EnumMember("FirstDayOfWeek", "Sunday"),
        ["vbMonday"]    = EnumMember("FirstDayOfWeek", "Monday"),
        ["vbTuesday"]   = EnumMember("FirstDayOfWeek", "Tuesday"),
        ["vbWednesday"] = EnumMember("FirstDayOfWeek", "Wednesday"),
        ["vbThursday"]  = EnumMember("FirstDayOfWeek", "Thursday"),
        ["vbFriday"]    = EnumMember("FirstDayOfWeek", "Friday"),
        ["vbSaturday"]  = EnumMember("FirstDayOfWeek", "Saturday"),

        // Tristate — TriState enum
        ["vbUseDefault"] = EnumMember("TriState", "UseDefault"),
        ["vbTrue"]       = EnumMember("TriState", "True"),
        ["vbFalse"]      = EnumMember("TriState", "False"),

        // String comparison — CompareMethod enum
        ["vbBinaryCompare"]   = EnumMember("CompareMethod", "Binary"),
        ["vbTextCompare"]     = EnumMember("CompareMethod", "Text"),
        ["vbDatabaseCompare"] = Lit(2, "vbDatabaseCompare"), // no .NET equivalent

        // String conversion — VbStrConv enum
        ["vbUpperCase"]    = EnumMember("VbStrConv", "UpperCase"),
        ["vbLowerCase"]    = EnumMember("VbStrConv", "LowerCase"),
        ["vbProperCase"]   = EnumMember("VbStrConv", "ProperCase"),
        ["vbWide"]         = EnumMember("VbStrConv", "Wide"),
        ["vbNarrow"]       = EnumMember("VbStrConv", "Narrow"),
        ["vbKatakana"]     = EnumMember("VbStrConv", "Katakana"),
        ["vbHiragana"]     = EnumMember("VbStrConv", "Hiragana"),
        ["vbUnicode"]      = EnumMember("VbStrConv", "Unicode"),
        ["vbFromUnicode"]  = EnumMember("VbStrConv", "FromUnicode"),

        // Date format — DateFormat enum
        ["vbGeneralDate"] = EnumMember("DateFormat", "GeneralDate"),
        ["vbLongDate"]    = EnumMember("DateFormat", "LongDate"),
        ["vbShortDate"]   = EnumMember("DateFormat", "ShortDate"),
        ["vbLongTime"]    = EnumMember("DateFormat", "LongTime"),
        ["vbShortTime"]   = EnumMember("DateFormat", "ShortTime"),

        // File / directory attributes — FileAttribute enum
        ["vbNormal"]    = EnumMember("FileAttribute", "Normal"),
        ["vbReadOnly"]  = EnumMember("FileAttribute", "ReadOnly"),
        ["vbHidden"]    = EnumMember("FileAttribute", "Hidden"),
        ["vbSystem"]    = EnumMember("FileAttribute", "System"),
        ["vbVolume"]    = EnumMember("FileAttribute", "Volume"),
        ["vbDirectory"] = EnumMember("FileAttribute", "Directory"),
        ["vbArchive"]   = EnumMember("FileAttribute", "Archive"),
        ["vbAlias"]     = EnumMember("FileAttribute", "Alias"),

        // Shell window style — AppWinStyle enum
        ["vbHide"]             = EnumMember("AppWinStyle", "Hide"),
        ["vbNormalFocus"]      = EnumMember("AppWinStyle", "NormalFocus"),
        ["vbMinimizedFocus"]   = EnumMember("AppWinStyle", "MinimizedFocus"),
        ["vbMaximizedFocus"]   = EnumMember("AppWinStyle", "MaximizedFocus"),
        ["vbNormalNoFocus"]    = EnumMember("AppWinStyle", "NormalNoFocus"),
        ["vbMinimizedNoFocus"] = EnumMember("AppWinStyle", "MinimizedNoFocus"),

        // VarType constants — integer literals (VariantType enum member names differ from VB6)
        ["vbEmpty"]      = Lit(0,    "vbEmpty"),
        ["vbNull"]       = Lit(1,    "vbNull"),
        ["vbInteger"]    = Lit(2,    "vbInteger"),
        ["vbLong"]       = Lit(3,    "vbLong"),
        ["vbSingle"]     = Lit(4,    "vbSingle"),
        ["vbDouble"]     = Lit(5,    "vbDouble"),
        ["vbCurrency"]   = Lit(6,    "vbCurrency"),
        ["vbDate"]       = Lit(7,    "vbDate"),
        ["vbString"]     = Lit(8,    "vbString"),
        ["vbObject"]     = Lit(9,    "vbObject"),
        ["vbError"]      = Lit(10,   "vbError"),
        ["vbBoolean"]    = Lit(11,   "vbBoolean"),
        ["vbVariant"]    = Lit(12,   "vbVariant"),
        ["vbDataObject"] = Lit(13,   "vbDataObject"),
        ["vbDecimal"]    = Lit(14,   "vbDecimal"),
        ["vbByte"]       = Lit(17,   "vbByte"),
        ["vbArray"]      = Lit(8192, "vbArray"),

        // Misc
        ["vbObjectError"] = Lit(-2147221504, "vbObjectError"), // 0x80040000
    };

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            // Skip replacement when the identifier appears in a name/declaration context
            // (namespace, using directive, qualified name) where only NameSyntax is accepted.
            if (node.Parent is QualifiedNameSyntax
                    || node.Parent is FileScopedNamespaceDeclarationSyntax
                    || node.Parent is NamespaceDeclarationSyntax
                    || node.Parent is UsingDirectiveSyntax
                    || node.Parent is AliasQualifiedNameSyntax) {
                return base.VisitIdentifierName(node);
            }

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
