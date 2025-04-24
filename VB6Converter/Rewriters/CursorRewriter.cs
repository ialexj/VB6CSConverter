using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VB6Converter.Conversion;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;
public class CursorRewriter : CSharpSyntaxRewriter
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
    {
        if (node.Expression is IdentifierNameSyntax lname) {
            var ids = (lname.Identifier.Text, node.Name.Identifier.Text);
            if (ids == ("Screen", "MousePointer")) {
                return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Cursor"),
                    IdentifierName("Current"))
                    .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "System.Windows.Forms"));
            }
        }

        return base.VisitMemberAccessExpression(node);
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (cursors.TryGetValue(node.Identifier.Text, out var cursor)) {
            if (cursor != string.Empty) {
                return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Cursors"), IdentifierName(cursor));
            }
            else {
                return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Cursors"), IdentifierName("Default"))
                    .WithTrailingTrivia(TriviaList(Comment($" // Not supported: {node.Identifier.Text}")));
            }
        }

        return base.VisitIdentifierName(node);
    }
}
