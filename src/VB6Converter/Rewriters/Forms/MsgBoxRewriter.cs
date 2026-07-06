using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.Rewriters.Forms;

public class MsgBoxRewriter : LoggedRewriter
{
    static readonly Dictionary<string, string> _results = new(StringComparer.InvariantCultureIgnoreCase) {
        { "vbOK",     "System.Windows.Forms.DialogResult.OK" },
        { "vbCancel", "System.Windows.Forms.DialogResult.Cancel" },
        { "vbYes",    "System.Windows.Forms.DialogResult.Yes" },
        { "vbNo",     "System.Windows.Forms.DialogResult.No" },
        { "vbAbort",  "System.Windows.Forms.DialogResult.Abort" },
        { "vbRetry",  "System.Windows.Forms.DialogResult.Retry" },
        { "vbIgnore", "System.Windows.Forms.DialogResult.Ignore" },
    };

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression)) {
            if (node.Left is IdentifierNameSyntax left && _results.TryGetValue(left.Identifier.Text, out string newLeft)) {
                node = node.WithLeft(ParseExpression(newLeft));
            }
            else if (node.Right is IdentifierNameSyntax right && _results.TryGetValue(right.Identifier.Text, out string newRight)) {
                node = node.WithRight(ParseExpression(newRight));
            }
        }

        return base.VisitBinaryExpression(node);
    }

    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is IdentifierNameSyntax name && string.Equals(name.Identifier.Text, "MsgBox", StringComparison.InvariantCultureIgnoreCase)) {
                return ConvertMsgBox(node);
            }

            return base.VisitInvocationExpression(node);
        });

    SyntaxNode ConvertMsgBox(InvocationExpressionSyntax node)
    {
        var arguments = node.ArgumentList;
        var message  = arguments.Arguments.Count >= 1 ? arguments.Arguments[0] : null;
        var buttons  = arguments.Arguments.Count >= 2 ? arguments.Arguments[1] : null;
        var title    = arguments.Arguments.Count >= 3 ? arguments.Arguments[2] : null;
        var helpfile = arguments.Arguments.Count >= 4 ? arguments.Arguments[3] : null;
        var context  = arguments.Arguments.Count >= 5 ? arguments.Arguments[4] : null;

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
                case "vbOkOnly": buttonArg = "System.Windows.Forms.MessageBoxButtons.OK"; break;
                case "vbOkCancel": buttonArg = "System.Windows.Forms.MessageBoxButtons.OKCancel"; break;
                case "vbYesNo": buttonArg = "System.Windows.Forms.MessageBoxButtons.YesNo"; break;
                case "vbYesNoCancel": buttonArg = "System.Windows.Forms.MessageBoxButtons.YesNoCancel"; break;
                case "vbRetryCancel": buttonArg = "System.Windows.Forms.MessageBoxButtons.RetryCancel"; break;
                case "vbAbortRetryIgnore": buttonArg = "System.Windows.Forms.MessageBoxButtons.AbortRetryIgnore"; break;

                case "vbInformation": iconArg = "System.Windows.Forms.MessageBoxIcon.Information"; break;
                case "vbQuestion": iconArg = "System.Windows.Forms.MessageBoxIcon.Question"; break;
                case "vbExclamation": iconArg = "System.Windows.Forms.MessageBoxIcon.Exclamation"; break;
                case "vbWarning": iconArg = "System.Windows.Forms.MessageBoxIcon.Warning"; break;
                case "vbCritical": iconArg = "System.Windows.Forms.MessageBoxIcon.Error"; break;
            }
        }

        IEnumerable<ArgumentSyntax> GetFinalArgs()
        {
            var def = Argument(LiteralExpression(
                SyntaxKind.DefaultLiteralExpression,
                Token(SyntaxKind.DefaultKeyword)));

            yield return message ?? def;
            yield return title ?? def;
            yield return buttonArg != null ? Argument(ParseName(buttonArg)) : def;

            if (iconArg != null) {
                yield return Argument(ParseName(iconArg));
            }
        }

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ParseExpression("System.Windows.Forms.MessageBox"), IdentifierName("Show")))
                .WithArgumentList(ArgumentList(GetFinalArgs().ToArray()))
                .WithAdditionalAnnotations(new SyntaxAnnotation("MessageBox", null));
    }
}
