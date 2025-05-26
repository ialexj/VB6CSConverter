using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.RoslynHelpers;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

public static class ValueConverter
{
    public static IdentifierNameSyntax GetIdentifierName(ICallTargetContext target)
    {
        return CommonConverter.GetIdentifierName(target.identifier(), target.typeHint()); // todo: handle cast
    }

    public static ExpressionSyntax GetCallInvocationExpression(ICallContext call, CallContext context)
    {
        using var _ = new TraceMethod(call);

        var invocation = call.segments().LastOrDefault();
        var args = invocation?.args().FirstOrDefault();

        var identifier = GetCallIdentifierExpression(call, context);

        if (invocation is ICS_S_ProcedureOrArrayCallContext
            || invocation is ECS_ProcedureCallContext
            || invocation is ECS_MemberProcedureCallContext
            || invocation is ICS_B_ProcedureCallContext
            || invocation is ICS_B_MemberProcedureCallContext
            || args?.ChildCount > 0) {

            var argList = GetArgumentList(args, context);
            return InvocationExpression(identifier).WithArgumentList(argList);
        }
        else {
            return identifier;
        }
    }

    public static ExpressionSyntax GetCallIdentifierExpression(ICallContext call, CallContext ctx, bool preferArray = false)
    {
        using var _ = new TraceMethod(call);

        var segments = call.segments().ToArray();
        if (call.IsPartial && ctx.With is not null) {
            segments = ctx.With.segments().Concat(segments).ToArray();
        }

        ExpressionSyntax expr = null;

        var invocation = segments.LastOrDefault();
        foreach (var s in segments) {
            var id = GetIdentifierName(s);

            if (expr is not null) {
                expr = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expr, id);
            }
            else {
                expr = id;
            }

            if (s is ICS_S_ProcedureOrArrayCallContext procOrArray && procOrArray.argsCall().Length > 0 && (preferArray || s != invocation)) {
                expr = ElementAccessExpression(expr, GetBracketedArgumentList(s.args().FirstOrDefault(), ctx));
            }
        }

        if (call.dictionaryCallStmt() is DictionaryCallStmtContext dictCall) {
            var text = dictCall.ambiguousIdentifier().GetText();

            expr = ElementAccessExpression(expr, BracketedArgumentList(
                SingletonSeparatedList(
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text)))
                )
            ));
        }

        return expr;
    }

    public static ExpressionSyntax GetValue(ValueStmtContext valueCtx, CallContext ctx)
    {
        using var _ = new TraceMethod(valueCtx);

        if (valueCtx is null) {
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (valueCtx is VsICSContext vsics) {
            return GetCallInvocationExpression(vsics.implicitCallStmt_InStmt(), ctx);
        }
        else if (valueCtx is VsLiteralContext literal) {
            return GetLiteral(literal.literal());
        }
        else if (valueCtx is VsNewContext @new) {
            return ObjectCreationExpression(IdentifierName(@new.valueStmt().GetText()))
                .WithArgumentList(ArgumentList());
        }
        else if (valueCtx is VsStructContext @struct) {
            var value = GetValue(@struct.valueStmt(0), ctx);
            var expr = ParenthesizedExpression(value);

            if (@struct.valueStmt().Length > 1) {
                expr = expr.WithError(TransformError.Create(@struct, "Struct with more than one value."));
            }

            return expr;
        }
        else if (valueCtx is IOperatorContext oper) {
            return GetOperator(oper, ctx);
        }
        else if (valueCtx is VsAssignContext assign) {
            // Just unpack the value - sometimes assignment is inside the expression for some reason
            // The argument list handles it at entry.
            return GetValue(assign.valueStmt(), ctx);
        }
        else {
            return ParseExpression(valueCtx.GetText())
                .WithError(TransformError.Create(valueCtx, "Unknown value"));
        }
    }

    public static ExpressionSyntax GetLiteral(LiteralContext lit)
    {
        if (lit.TRUE() is not null) {
            return LiteralExpression(SyntaxKind.TrueLiteralExpression);
        }
        else if (lit.FALSE() is not null) {
            return LiteralExpression(SyntaxKind.FalseLiteralExpression);
        }
        else if (lit.NOTHING() is not null || lit.NULL() is not null) {
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (lit.INTEGERLITERAL() is ITerminalNode @short) {

            var text = @short.Symbol.Text;
            text = text.TrimEnd('&');

            if (text.StartsWith('&')) {
                string hex = text[1..];
                int value = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal("0x" + hex, value));
            }
            else {
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt32(text)));
            }
        }
        else if (lit.DOUBLELITERAL() is ITerminalNode @double) {
            return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToDouble(@double.GetText().TrimEnd(['&']))));
        }
        else if (lit.STRINGLITERAL() is ITerminalNode @string) {
            string str = @string.Symbol.Text;
            str = str.Trim('"');
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(str));
        }
        else if (lit.DATELITERAL() is ITerminalNode @date) {
            var text = date.Symbol.Text.Trim('#');
            return InvocationExpression(
                ParseExpression("DateTime.Parse"),
                ArgumentList(
                    LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text))
                ));
        }
        else if (lit.FILENUMBER() is ITerminalNode file) {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(file.GetText().TrimStart('#')));
        }
        else if (lit.COLORLITERAL() is ITerminalNode color) {

            var text = color.Symbol.Text;
            text = text.TrimEnd('&');

            if (text.StartsWith('&')) {
                string hex = text.TrimStart(['&', 'H']);
                long value = long.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal("0x" + hex, value));
            }
            else {
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt64(text)));
            }

            //var hex = "0x" + color.Symbol.Text[2..].TrimEnd(['&']).PadLeft(6, '0').PadLeft(8, 'F');
            //var col = System.Drawing.Color.FromArgb(Convert.ToInt32(hex, 16));

            //if (col.IsNamedColor) {
            //    return InvocationExpression(
            //        MemberAccessExpression(
            //            SyntaxKind.SimpleMemberAccessExpression,
            //            IdentifierName("Color"), IdentifierName("FromName")
            //        ))
            //        .WithArgumentList(RoslynHelpers.ArgumentList(
            //            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(col.Name))
            //        ));
            //}
            //else if (col.A == 255) {
            //    return InvocationExpression(
            //        MemberAccessExpression(
            //            SyntaxKind.SimpleMemberAccessExpression,
            //            IdentifierName("Color"), IdentifierName("FromArgb")
            //        ))
            //        .WithArgumentList(RoslynHelpers.ArgumentList(
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.R)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.G)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.B))
            //        ));
            //}
            //else {
            //    return InvocationExpression(
            //        MemberAccessExpression(
            //            SyntaxKind.SimpleMemberAccessExpression,
            //            IdentifierName("Color"), IdentifierName("FromArgb")
            //        ))
            //        .WithArgumentList(RoslynHelpers.ArgumentList(
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.A)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.R)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.G)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.B))
            //        ));
            //}
        }
        else {
            return ParseExpression(lit.GetText())
                .WithError(TransformError.Create(lit, "Unknown literal type"));
        }
    }

    public static ExpressionSyntax GetOperator(IOperatorContext oper, CallContext expr)
    {
        var values = oper.valueStmt().Select(v => GetValue(v, expr)).ToArray();

        // Try to invert the inner expression
        if (oper is VsNotContext not && values.Length == 1) {
            if (values[0] is BinaryExpressionSyntax bin) {
                if (bin.IsKind(SyntaxKind.EqualsExpression)) {
                    return BinaryExpression(SyntaxKind.NotEqualsExpression, bin.Left, bin.Right);
                }
                else if (bin.IsKind(SyntaxKind.NotEqualsExpression)) {
                    return BinaryExpression(SyntaxKind.EqualsExpression, bin.Left, bin.Right);
                }
                else if (bin.IsKind(SyntaxKind.IsExpression)) {
                    if (bin.Right is LiteralExpressionSyntax lit) {
                        return IsPatternExpression(bin.Left, UnaryPattern(ConstantPattern(lit)));
                    }
                }
            }
        }

        // is null
        IsPatternExpression(IdentifierName("a"),
            ConstantPattern(
                LiteralExpression(
                    SyntaxKind.NullLiteralExpression)));

        // is not null
        IsPatternExpression(IdentifierName("a"),
            UnaryPattern(
                ConstantPattern(
                    LiteralExpression(
                        SyntaxKind.NullLiteralExpression))));


        

        if (oper is VsPowContext pow) {
            return InvocationExpression(ParseName("Math.Pow"), ArgumentList(values));
        }

        SyntaxKind kind = oper switch {
            VsAmpContext => SyntaxKind.AddExpression, // string concat

            VsAddContext => SyntaxKind.AddExpression,
            VsMinusContext => SyntaxKind.SubtractExpression,
            VsMultContext => SyntaxKind.MultiplyExpression,
            VsDivContext => SyntaxKind.DivideExpression,
            VsModContext => SyntaxKind.ModuloExpression,

            VsNegationContext => SyntaxKind.UnaryMinusExpression,

            VsEqContext => SyntaxKind.EqualsExpression,
            VsNeqContext => SyntaxKind.NotEqualsExpression,
            VsGtContext => SyntaxKind.GreaterThanExpression,
            VsGeqContext => SyntaxKind.GreaterThanOrEqualExpression,
            VsLtContext => SyntaxKind.LessThanExpression,
            VsLeqContext => SyntaxKind.LessThanOrEqualExpression,

            VsAndContext => SyntaxKind.LogicalAndExpression,
            VsOrContext => SyntaxKind.LogicalOrExpression,
            VsNotContext => SyntaxKind.LogicalNotExpression,
            VsXorContext => SyntaxKind.ExclusiveOrExpression,

            VsIsContext => SyntaxKind.IsExpression,

            _ => throw new TransformException(oper, "Invalid operator.")
        };

        if (values.Length == 1) {
            return PrefixUnaryExpression(kind, values[0]);
        }
        else if (values.Length == 2) {
            return BinaryExpression(kind, values[0], values[1]);
        }
        else {
            throw new TransformException(oper, "Invalid operator type");
        }
    }

    static ArgumentListSyntax GetArgumentList(ArgsCallContext args, CallContext ctx) => ArgumentList(GetArgumentListCore(args, ctx));

    static BracketedArgumentListSyntax GetBracketedArgumentList(ArgsCallContext args, CallContext ctx) => BracketedArgumentList(GetArgumentListCore(args, ctx));

    static SeparatedSyntaxList<ArgumentSyntax> GetArgumentListCore(ArgsCallContext args, CallContext ctx)
    {
        if (args is null)
            return SeparatedList<ArgumentSyntax>();

        IEnumerable<ArgumentSyntax> GetArgsCore()
            => args.argCall().Select(arg => {
                var value = arg.valueStmt();

                // Sometimes the assignment is inside the expression rather at the top level
                var assign = value as VsAssignContext ?? value.children.OfType<VsAssignContext>().FirstOrDefault();
                if (assign is not null) {
                    var argName = GetCallIdentifierExpression(assign.implicitCallStmt_InStmt(), ctx);
                    var argValue = GetValue(assign.valueStmt(), ctx);
                    return Argument(argValue).WithNameColon(NameColon((IdentifierNameSyntax)argName));
                }
                else {
                    return Argument(GetValue(arg.valueStmt(), ctx));
                }
            });

        return SeparatedList<ArgumentSyntax>(GetArgsCore()
            .Select(a => (SyntaxNodeOrToken)a)
            .Intersperse(Token(SyntaxKind.CommaToken)));
    }
}
