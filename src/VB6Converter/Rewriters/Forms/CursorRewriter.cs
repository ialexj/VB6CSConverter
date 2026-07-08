using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Forms;

public class CursorRewriter(string file) : LoggedRewriter(file)
{
    static readonly Dictionary<string, string> cursors = new(StringComparer.InvariantCultureIgnoreCase) {
        ["vbDefault"] = "Default",
        ["vbArrow"] = "Arrow",
        ["vbCrosshair"] = "Crosshair",
        ["vbIbeam"] = "IBeam",
        ["vbSizeAll"] = "SizeAll",
        ["vbSizePointer"] = "SizeAll",
        ["vbSizeNESW"] = "SizeNESW",
        ["vbSizeNS"] = "SizeNS",
        ["vbSizeNWSE"] = "SizeNWSE",
        ["vbSizeWE"] = "SizeWE",
        ["vbUpArrow"] = "UpArrow",
        ["vbHourglass"] = "WaitCursor",
        ["vbNoDrop"] = "No",
        ["vbArrowHourglass"] = "AppStarting",
        ["vbArrowQuestion"] = "Help",
        ["vbIconPointer"] = "",
        ["vbCustom"] = "",
    };

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is IdentifierNameSyntax lname) {
                /* Set */ var ids = (lname.Identifier.Text, node.Name.Identifier.Text);
                if (ids == ("Screen", "MousePointer")) {
                    return ParseExpression("System.Windows.Forms.Cursor.Current");
                }
            }

            return base.VisitMemberAccessExpression(node);
        });

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (cursors.TryGetValue(node.Identifier.Text, out var cursor)) {
                if (cursor != string.Empty) {
                    return ParseExpression($"System.Windows.Forms.Cursors.{cursor}");
                }
                else {
                    return ParseExpression("System.Windows.Forms.Cursors.Default")
                        .WithTrailingTrivia(TriviaList(Comment($" // Not supported: {node.Identifier.Text}")));
                }
            }

            return base.VisitIdentifierName(node);
        });
}
