using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter;

public class VBFunctionRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.Text == "vbNullString") {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(string.Empty));
        }
        else if (node.Identifier.Text == "vbNullChar") {
            return LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\0'));
        }
        else if (node.Identifier.Text == "vbCr") {
            return LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\r'));
        }
        else if (node.Identifier.Text == "vbLf") {
            return LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\n'));
        }
        else if (node.Identifier.Text == "vbCrLf") {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("\r\n"));
        }
        else if (node.Identifier.Text == "vbFormFeed") {
            return LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal("\f"));
        }
        else if (node.Identifier.Text == "vbBack") {
            return LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\b'));
        }
        else if (node.Identifier.Text == "vbTab") {
            return LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal('\t'));
        }

        // DAO

        else if (node.Identifier.Text == "Recordset") {
            return node.WithAdditionalAnnotations(
                new SyntaxAnnotation("Using", "Microsoft.Office.Interop.Access.Dao")
            );
        }

        string[] RecordsetOptionEnum = [
            "dbDenyWrite",
            "dbDenyRead",
            "dbReadOnly",
            "dbAppendOnly",
            "dbInconsistent",
            "dbConsistent",
            "dbSQLPassThrough",
            "dbFailOnError",
            "dbForwardOnly ",
            "dbSeeChanges",
            "dbRunAsync",
            "dbExecDirect"
        ];

        if (RecordsetOptionEnum.Contains(node.Identifier.Text)) {
            return ParseExpression("RecordsetOptionEnum." + node.Identifier.Text)
                .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "Microsoft.Office.Interop.Access.Dao"));
        }

        string[] RecordsetTypeEnum = [
            "dbOpenTable",
            "dbOpenSnapshot",
            "dbOpenForwardOnly",
            "dbOpenDynamic",
            "dbOpenDynaset",
            "dbOpenKeyset"
        ];

        if (RecordsetTypeEnum.Contains(node.Identifier.Text)) {
            return ParseExpression("RecordsetTypeEnum." + node.Identifier.Text)
                .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "Microsoft.Office.Interop.Access.Dao"));
        }

        return base.VisitIdentifierName(node);
    }


    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is not IdentifierNameSyntax name)
            return base.VisitInvocationExpression(node);

        return name.Identifier.Text switch {
            "Replace" => ConvertReplace(node),

            "IsNull" => ConvertIsNull(node),
            "IsArray" => ConvertIsArray(node),

            "UBound" => ConvertUBound(node),

            "DateSerial" => ConvertDateSerial(node),

            "Hour" => ConvertToMemberAccess(node),
            "Minute" => ConvertToMemberAccess(node),
            "Second" => ConvertToMemberAccess(node),
            "Year" => ConvertToMemberAccess(node),
            "Month" => ConvertToMemberAccess(node),
            "Day" => ConvertToMemberAccess(node),

            "Now" => ParseExpression("DateTime.Now"),
            "Date" => ParseExpression("DateTime.Now.Date"),

            "Len" => ConvertLen(node),
            "Left" => ConvertLeft(node),

            "CStr" => ConvertToMemberAccess(node, "Convert.ToString"),
            "CLng" => ConvertToMemberAccess(node, "Convert.ToInt32"),
            "CDbl" => ConvertToMemberAccess(node, "Convert.ToDouble"),

            "IIf" => ConvertIIf(node),


            "MsgBox" => ConvertMsgBox(node),
            _ => base.VisitInvocationExpression(node),
        };
    }

    private SyntaxNode ConvertIIf(InvocationExpressionSyntax node)
    {
        var condition = node.ArgumentList.Arguments[0].Expression;
        var trueValue = node.ArgumentList.Arguments[1].Expression;
        var falseValue = node.ArgumentList.Arguments[2].Expression;

        return ConditionalExpression(condition, trueValue, falseValue);
    }

    private SyntaxNode ConvertToMemberAccess(InvocationExpressionSyntax node, string expression)
        => InvocationExpression(
            ParseExpression(expression),
            ArgumentList(node.ArgumentList.Arguments[0].Expression));

    private SyntaxNode ConvertToMemberAccess(InvocationExpressionSyntax node) 
    {
        if (node.Expression is IdentifierNameSyntax name) {
            var value = node.ArgumentList.Arguments[0];
            return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, value.Expression, name);
        }
        else {
            return node;
        }
    }


    private SyntaxNode ConvertDateSerial(InvocationExpressionSyntax node)
    {
        var year = node.ArgumentList.Arguments[0];
        var month = node.ArgumentList.Arguments[1];
        var day = node.ArgumentList.Arguments[2];

        return ObjectCreationExpression(
            IdentifierName("DateTime"),
            ArgumentList(year, month, day),
            null);
    }


    private SyntaxNode ConvertLen(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];

        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParenthesizedExpression(CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression)),
            IdentifierName("Length"));
    }

    private SyntaxNode ConvertLeft(InvocationExpressionSyntax node)
    {
        var str = node.ArgumentList.Arguments[0];
        var len = node.ArgumentList.Arguments[1];

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression)),
                IdentifierName("Substring")),
            ArgumentList(
                Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))),
                len));        
    }

    private SyntaxNode ConvertUBound(InvocationExpressionSyntax node)
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

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(CastExpression(PredefinedType(Token(SyntaxKind.StringKeyword)), str.Expression)),
                IdentifierName("Replace")),
            ArgumentList(oldValue, newValue)
        );
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
        => BinaryExpression(
            SyntaxKind.IsExpression,
            node.ArgumentList.Arguments[0].Expression,
            IdentifierName(nameof(Array)));

    SyntaxNode ConvertMsgBox(InvocationExpressionSyntax node)
    {
        var arguments = node.ArgumentList;
        var message = arguments.Arguments.Count >= 1 ? arguments.Arguments[0] : null;
        var buttons = arguments.Arguments.Count >= 2 ? arguments.Arguments[1] : null;
        var title = arguments.Arguments.Count >= 3 ? arguments.Arguments[2] : null;
        var helpfile = arguments.Arguments.Count >= 4 ? arguments.Arguments[3] : null;
        var context = arguments.Arguments.Count >= 5 ? arguments.Arguments[4] : null;

        List<string> options = [];

        if (buttons?.Expression is BinaryExpressionSyntax binary) {
            if (binary.Left is IdentifierNameSyntax l) {
                options.Add(l.Identifier.Text);
            }
            if (binary.Right is IdentifierNameSyntax r) {
                options.Add(r.Identifier.Text);
            }
        }
        else if (buttons?.Expression is IdentifierNameSyntax b) {
            options.Add(b.Identifier.Text);
        }

        string buttonArg = null;
        string iconArg = null;

        foreach (var option in options) {
            switch (option) {
                case "vbOkOnly": buttonArg = "MessageBoxButtons.OK"; break;
                case "vbOkCancel": buttonArg = "MessageBoxButtons.OKCancel"; break;
                case "vbYesNo": buttonArg = "MessageBoxButtons.YesNo"; break;
                case "vbYesNoCancel": buttonArg = "MessageBoxButtons.YesNoCancel"; break;
                case "vbRetryCancel": buttonArg = "MessageBoxButtons.RetryCancel"; break;
                case "vbAbortRetryIgnore": buttonArg = "MessageBoxButtons.AbortRetryIgnore"; break;

                case "vbInformation": iconArg = "MessageBoxIcon.Information"; break;
                case "vbQuestion": iconArg = "MessageBoxIcon.Question"; break;
                case "vbExclamation": iconArg = "MessageBoxIcon.Exclamation"; break;
                case "vbWarning": iconArg = "MessageBoxIcon.Warning"; break;
                case "vbCritical": iconArg = "MessageBoxIcon.Error"; break;
            }
        }

        IEnumerable<ArgumentSyntax> GetFinalArgs()
        {
            yield return message;
            if (title != null) {
                yield return title;
            }
            if (buttonArg != null) {
                yield return Argument(ParseName(buttonArg));
            }
            if (iconArg != null) {
                yield return Argument(ParseName(iconArg));
            }
        }

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("MessageBox"), IdentifierName("Show")))
            .WithArgumentList(ArgumentList(GetFinalArgs().ToArray()));
    }
}
