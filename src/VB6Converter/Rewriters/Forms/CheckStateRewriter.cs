using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Forms;

public class CheckStateRewriter : LoggedRewriter
{
    static readonly Dictionary<string, string> _checkStates = new(StringComparer.InvariantCultureIgnoreCase) {
        ["vbUnchecked"] = "Unchecked",
        ["vbChecked"]   = "Checked",
        ["vbGrayed"]    = "Indeterminate",
    };

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is QualifiedNameSyntax
                    || node.Parent is FileScopedNamespaceDeclarationSyntax
                    || node.Parent is NamespaceDeclarationSyntax
                    || node.Parent is UsingDirectiveSyntax
                    || node.Parent is AliasQualifiedNameSyntax) {
                return base.VisitIdentifierName(node);
            }

            if (_checkStates.TryGetValue(node.Identifier.Text, out var member)) {
                return ParseExpression($"System.Windows.Forms.CheckState.{member}");
            }

            return base.VisitIdentifierName(node);
        });
}
