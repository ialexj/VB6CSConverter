using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Rewriters;
public class ForEachVariableRewriter : LoggedRewriter
{
    public override SyntaxNode VisitBlock(BlockSyntax block)
    {
        var foreaches = block.ChildNodes().OfType<ForEachStatementSyntax>();

        if (foreaches.Any()) {
            var vars = new List<string>();

            foreach (var fe in foreaches) {
                if (FindDeclarator(block, fe.Identifier.Text) is VariableDeclaratorSyntax decl) {
                    block = block.ReplaceNode(fe, fe.WithType(((VariableDeclarationSyntax)decl.Parent).Type));
                    vars.Add(fe.Identifier.Text);
                }
            }

            // Remove declarations.
            // There might be a situation where the same variable is used elsewhere.
            // We would need to use the semantic model to figure that out.

            foreach (var variable in vars) {
                var decl = FindDeclarator(block, variable);
                if (decl != null) {


                    var local = decl.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
                
                    if (local.Declaration.Variables.Count == 1) {
                        block = block.RemoveNode(local, SyntaxRemoveOptions.KeepNoTrivia);
                    }
                    else {
                        block = block.ReplaceNode(local, local.WithDeclaration(
                            local.Declaration.RemoveNode(decl, SyntaxRemoveOptions.KeepNoTrivia)
                        ));
                    }
                }
            }
        }

        return base.VisitBlock(block);
    }

    static VariableDeclaratorSyntax FindDeclarator(BlockSyntax block, string name)
        => block.ChildNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(d => d.Declaration.Variables)
            .FirstOrDefault(v => v.Identifier.Text.Equals(name));
}
