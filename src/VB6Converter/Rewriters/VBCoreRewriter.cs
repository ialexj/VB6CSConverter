using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.Rewriters;

public class VBCoreRewriter(string file = null) : LoggedRewriter(file)
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name == node) {
                return base.VisitIdentifierName(node);
            }

            return node.Identifier.Text switch {
                "Now"  => ParseExpression("System.DateTime.Now"),
                "Date" => ParseExpression("System.DateTime.Now.Date"),
                "Time" => ParseExpression("System.DateTime.Now.TimeOfDay"),
                _ => base.VisitIdentifierName(node),
            };
        });

    static readonly Dictionary<string, Func<InvocationExpressionSyntax, SyntaxNode>> _funcs = new(StringComparer.OrdinalIgnoreCase) {
        ["Array"] = ConvertArray,
        ["Replace"] = ConvertReplace,

        ["IsNull"] = ConvertIsNull,
        ["IsArray"] = node => ConvertIs(node, IdentifierName(nameof(Array))),

        ["UBound"] = ConvertUBound,

        ["DateSerial"] = ConvertDateSerial,

        ["Hour"] = ConvertToMemberAccess,
        ["Minute"] = ConvertToMemberAccess,
        ["Second"] = ConvertToMemberAccess,
        ["Year"] = ConvertToMemberAccess,
        ["Month"] = ConvertToMemberAccess,
        ["Day"] = ConvertToMemberAccess,

        ["String"] = ConvertString,
        ["Format"] = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.Strings.Format"), node.ArgumentList),
        ["Len"] = ConvertLen,
        ["Left"] = ConvertLeft,

        ["CStr"] = node => ConvertToMemberAccess(node, "Convert.ToString"),
        ["CLng"] = node => ConvertToMemberAccess(node, "Convert.ToInt32"),
        ["CDbl"] = node => ConvertToMemberAccess(node, "Convert.ToDouble"),
        ["Val"] = node => ConvertToMemberAccess(node, "Microsoft.VisualBasic.Conversion.Val"),

        ["IIf"] = ConvertIIf,

        ["Asc"] = ConvertAsc,
        ["Chr"] = ConvertChr,

        ["Str"] = ConvertStr,

        ["IsMissing"] = ConvertIsMissing,

        // Math
        ["Abs"]   = node => ConvertToMemberAccess(node, "Math.Abs"),
        ["Sin"]   = node => ConvertToMemberAccess(node, "Math.Sin"),
        ["Cos"]   = node => ConvertToMemberAccess(node, "Math.Cos"),
        ["Tan"]   = node => ConvertToMemberAccess(node, "Math.Tan"),
        ["Atn"]   = node => ConvertToMemberAccess(node, "Math.Atan"),
        ["Sqr"]   = node => ConvertToMemberAccess(node, "Math.Sqrt"),
        ["Log"]   = node => ConvertToMemberAccess(node, "Math.Log"),
        ["Exp"]   = node => ConvertToMemberAccess(node, "Math.Exp"),
        ["Sgn"]   = node => ConvertToMemberAccess(node, "Math.Sign"),
        ["Int"]   = ConvertInt,
        ["Fix"]   = ConvertFix,
        ["Round"] = ConvertRound,

        // Type conversions
        ["CInt"]   = node => ConvertToMemberAccess(node, "Convert.ToInt32"),
        ["CShort"] = node => ConvertToMemberAccess(node, "Convert.ToInt16"),
        ["CSng"]   = node => ConvertToMemberAccess(node, "Convert.ToSingle"),
        ["CBool"]  = node => ConvertToMemberAccess(node, "Convert.ToBoolean"),
        ["CByte"]  = node => ConvertToMemberAccess(node, "Convert.ToByte"),
        ["CDate"]  = node => ConvertToMemberAccess(node, "Convert.ToDateTime"),
        ["CCur"]   = node => ConvertToMemberAccess(node, "Convert.ToDecimal"),

        // Strings
        ["Trim"]  = node => ConvertStringMethod(node, "Trim"),
        ["LTrim"] = node => ConvertStringMethod(node, "LTrim"),
        ["RTrim"] = node => ConvertStringMethod(node, "RTrim"),
        ["LCase"] = node => ConvertStringMethod(node, "LCase"),
        ["UCase"] = node => ConvertStringMethod(node, "UCase"),
        ["Right"] = ConvertRight,
        ["Mid"]   = ConvertMid,
        ["Space"] = ConvertSpace,
        ["InStr"] = ConvertInStr,

        // FileSystem
        ["EOF"]          = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.EOF"),          node.ArgumentList),
        ["LOF"]          = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.LOF"),          node.ArgumentList),
        ["FreeFile"]     = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FreeFile"),     node.ArgumentList),
        ["FileLen"]      = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FileLen"),      node.ArgumentList),
        ["Seek"]         = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.Seek"),         node.ArgumentList),
        ["Dir"]          = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.Dir"),          node.ArgumentList),
        ["FileAttr"]     = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FileAttr"),     node.ArgumentList),
        ["FileDateTime"] = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.FileDateTime"), node.ArgumentList),
        ["GetAttr"]      = node => InvocationExpression(ParseExpression("Microsoft.VisualBasic.FileSystem.GetAttr"),      node.ArgumentList),
    };

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        => Rewrite(node, node => {
            SyntaxNode newSyntax = node;
            if (node.Expression is IdentifierNameSyntax name) {
                // Try to convert via just the name
                var expr = VisitIdentifierName(name);
                if (!name.IsEquivalentTo(expr)) {
                    return expr;
                }

                if (_funcs.TryGetValue(name.Identifier.Text, out var converter)) {
                    newSyntax = converter(node);
                }
            }

            if (newSyntax?.IsEquivalentTo(node) == false) {
                return Visit(newSyntax);
            }
            else {
                return base.VisitInvocationExpression(node);
            }
        });

    static SyntaxNode ConvertStr(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments[0].Expression;
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(arg), IdentifierName("ToString")),
            ArgumentList());
    }

    static SyntaxNode ConvertIsMissing(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments[0].Expression;
        return BinaryExpression(SyntaxKind.EqualsExpression, arg, LiteralExpression(SyntaxKind.DefaultLiteralExpression));
    }

    public override SyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        var result = base.VisitPrefixUnaryExpression(node);
        // When a rewriter (e.g. IsMissing → x == default) replaces an invocation with a bare
        // BinaryExpression, the ! operator would render as !x == default (wrong precedence).
        // Only invert when the operand is a *direct* (non-parenthesized) BinaryExpression,
        // so that explicit parens like Not (a = b) → !(a == b) are left unchanged.
        if (result is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.LogicalNotExpression)
            && unary.Operand is BinaryExpressionSyntax bin) {
            if (bin.IsKind(SyntaxKind.EqualsExpression)) {
                return BinaryExpression(SyntaxKind.NotEqualsExpression, bin.Left, bin.Right);
            }
            if (bin.IsKind(SyntaxKind.NotEqualsExpression)) {
                return BinaryExpression(SyntaxKind.EqualsExpression, bin.Left, bin.Right);
            }
        }
        return result;
    }

    static SyntaxNode ConvertChr(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments[0].Expression;
        if (arg is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.NumericLiteralExpression)) {
            return LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                Literal(Convert.ToChar((int)literal.Token.Value)));
        }
        else {
            return ParenthesizedExpression(
                CastExpression(PredefinedType(Token(SyntaxKind.CharKeyword)),
                arg));
        }
    }

    static SyntaxNode ConvertString(InvocationExpressionSyntax node)
    {
        var charExpr = CastExpression(
            PredefinedType(Token(SyntaxKind.CharKeyword)),
            node.ArgumentList.Arguments[1].Expression);
        return ObjectCreationExpression(
            PredefinedType(Token(SyntaxKind.StringKeyword)),
            ArgumentList(
                charExpr,
                node.ArgumentList.Arguments[0].Expression),
            null);
    }

    static SyntaxNode ConvertArray(InvocationExpressionSyntax node)
    {
        return ImplicitArrayCreationExpression(
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList<ExpressionSyntax>(
                    node.ArgumentList.Arguments.Select(a => (SyntaxNodeOrToken)a.Expression).Intersperse(Token(SyntaxKind.CommaToken))
                )
            )
        );
    }

    static SyntaxNode ConvertAsc(InvocationExpressionSyntax node)
    {
        var args = node.ArgumentList.Arguments[0].Expression;
        if (args is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            && literal.Token.ValueText.Length == 1) {
            return LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                Literal(literal.Token.ValueText[0]));
        }
        else {
            return node;
        }
    }

    static SyntaxNode ConvertIIf(InvocationExpressionSyntax node)
    {
        var condition = node.ArgumentList.Arguments[0].Expression;
        var trueValue = node.ArgumentList.Arguments[1].Expression;
        var falseValue = node.ArgumentList.Arguments[2].Expression;

        return ParenthesizedExpression(ConditionalExpression(condition, trueValue, falseValue));
    }

    static SyntaxNode ConvertToMemberAccess(InvocationExpressionSyntax node, string expression)
        => InvocationExpression(
            ParseExpression(expression),
            ArgumentList(node.ArgumentList.Arguments[0].Expression));

    static SyntaxNode ConvertToMemberAccess(InvocationExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax name) {
            var value = node.ArgumentList.Arguments[0];
            return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, value.Expression, name);
        }
        else {
            return node;
        }
    }

    static SyntaxNode ConvertDateSerial(InvocationExpressionSyntax node)
    {
        var year = node.ArgumentList.Arguments[0];
        var month = node.ArgumentList.Arguments[1];
        var day = node.ArgumentList.Arguments[2];

        return ObjectCreationExpression(
            IdentifierName("DateTime"),
            ArgumentList(year, month, day),
            null);
    }

    static SyntaxNode ConvertLen(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];
        return StringsCall("Len", CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression));
    }

    static SyntaxNode ConvertLeft(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];
        var len = node.ArgumentList.Arguments[1];
        return StringsCall("Left",
            CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression),
            len.Expression);
    }

    static SyntaxNode ConvertUBound(InvocationExpressionSyntax node)
    {
        var array = node.ArgumentList.Arguments[0];

        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParenthesizedExpression(CastExpression(IdentifierName("Array"), array.Expression)),
            IdentifierName("Length"));
    }

    static SyntaxNode ConvertReplace(InvocationExpressionSyntax node)
    {
        var str      = node.ArgumentList.Arguments[0];
        var oldValue = node.ArgumentList.Arguments[1];
        var newValue = node.ArgumentList.Arguments[2];
        return StringsCall("Replace",
            CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression),
            oldValue.Expression,
            newValue.Expression);
    }

    static SyntaxNode ConvertIsNull(InvocationExpressionSyntax node)
        => ParenthesizedExpression(
            IsPatternExpression(node.ArgumentList.Arguments[0].Expression, ConstantPattern(
                LiteralExpression(
                    SyntaxKind.NullLiteralExpression
                )
            )
        ));

    static SyntaxNode ConvertIsArray(InvocationExpressionSyntax node)
        => ParenthesizedExpression(
            BinaryExpression(
                SyntaxKind.IsExpression,
                node.ArgumentList.Arguments[0].Expression,
                IdentifierName(nameof(Array))));

    static SyntaxNode ConvertIs(InvocationExpressionSyntax node, IdentifierNameSyntax what)
        => ParenthesizedExpression(
            BinaryExpression(
                SyntaxKind.IsExpression,
                node.ArgumentList.Arguments[0].Expression,
                what));

    // ── Math ─────────────────────────────────────────────────────────────────────

    static SyntaxNode ConvertInt(InvocationExpressionSyntax node)
    {
        var n = node.ArgumentList.Arguments[0].Expression;
        return CastExpression(
            PredefinedType(Token(SyntaxKind.IntKeyword)),
            InvocationExpression(
                ParseExpression("Math.Floor"),
                ArgumentList(CastExpression(PredefinedType(Token(SyntaxKind.DoubleKeyword)), n))));
    }

    static SyntaxNode ConvertFix(InvocationExpressionSyntax node)
    {
        var n = node.ArgumentList.Arguments[0].Expression;
        return CastExpression(
            PredefinedType(Token(SyntaxKind.IntKeyword)),
            InvocationExpression(
                ParseExpression("Math.Truncate"),
                ArgumentList(CastExpression(PredefinedType(Token(SyntaxKind.DoubleKeyword)), n))));
    }

    static SyntaxNode ConvertRound(InvocationExpressionSyntax node)
        => InvocationExpression(ParseExpression("Math.Round"), node.ArgumentList);

    // ── Strings ──────────────────────────────────────────────────────────────────

    static InvocationExpressionSyntax StringsCall(string methodName, params ExpressionSyntax[] args)
        => InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("Microsoft.VisualBasic.Strings"),
                IdentifierName(methodName)),
            ArgumentList(args));

    static SyntaxNode ConvertStringMethod(InvocationExpressionSyntax node, string methodName)
    {
        var str = node.ArgumentList.Arguments[0];
        return StringsCall(methodName, CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression));
    }

    static SyntaxNode ConvertRight(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];
        var len = node.ArgumentList.Arguments[1];
        return StringsCall("Right",
            CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression),
            len.Expression);
    }

    static SyntaxNode ConvertMid(InvocationExpressionSyntax node)
    {
        var args  = node.ArgumentList.Arguments;
        var str   = args[0];
        var start = args[1].Expression;
        var castStr = CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression);
        return args.Count == 2
            ? StringsCall("Mid", castStr, start)
            : StringsCall("Mid", castStr, start, args[2].Expression);
    }

    static SyntaxNode ConvertSpace(InvocationExpressionSyntax node)
    {
        var len = node.ArgumentList.Arguments[0].Expression;
        return StringsCall("Space", len);
    }

    static SyntaxNode ConvertInStr(InvocationExpressionSyntax node)
    {
        var args = node.ArgumentList.Arguments;
        if (args.Count >= 3) {
            var start  = args[0].Expression;
            var str    = args[1].Expression;
            var search = args[2].Expression;
            return StringsCall("InStr", start,
                CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str),
                search);
        }
        else {
            var str    = args[0].Expression;
            var search = args[1].Expression;
            return StringsCall("InStr",
                CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str),
                search);
        }
    }
}
