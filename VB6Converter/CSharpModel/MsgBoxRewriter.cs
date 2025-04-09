using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.CSharpModel;

public class MsgBoxRewriter : IMethodRewriter
{
    public ExpressionSyntax RewriteIdentifier(ExpressionSyntax identifier)
    {
        if (identifier is IdentifierNameSyntax name && name.Identifier.Text == "MsgBox") {
            return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("MessageBox"), IdentifierName("Show"));
        }
        else {
            return null;
        }
    }
    
    public ArgumentListSyntax RewriteArguments(ArgumentListSyntax arguments, ExpressionSyntax identifier)
    {
        var list = new List<ArgumentSyntax>();

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

        return RoslynHelpers.ArgumentList(GetFinalArgs().ToArray());
    }

}
