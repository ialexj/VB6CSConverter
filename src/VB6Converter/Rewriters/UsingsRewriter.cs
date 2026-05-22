using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

/// <summary>
/// Updates usings from code annotations.
/// </summary>
public class UsingsRewriter(string file = null) : LoggedRewriter(file)
{
    public static readonly UsingsRewriter Default = new();

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
        => Rewrite(node, node => {
            // Make enum values as global constants
            IEnumerable<NameSyntax> GetGlobalStaticUsings()
            {
                foreach (var c in node.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
                    var ns = c.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
                    var cid = IdentifierName(c.Identifier);
                    NameSyntax classFullName = ns != null ? QualifiedName(ns.Name, cid) : cid;

                    if (c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) {
                        yield return classFullName;
                    }

                    var enums = c.DescendantNodes().OfType<EnumDeclarationSyntax>()
                        .Select(e => QualifiedName(classFullName, IdentifierName(e.Identifier)));

                    foreach (var e in enums) {
                        yield return e;
                    }
                }
            }

            var globalUsings = GetGlobalStaticUsings()
                .Select(n => UsingDirective(n)
                    .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
                    .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

            var localUsings = node.Usings.Where(n => !n.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).Select(n => n.Name.ToString())
                .Concat(node.GetAnnotatedNodesAndTokens("Using").SelectMany(t => t.GetAnnotations("Using")).Select(a => a.Data))
                .Concat([ "System" ])
                .Distinct()
                .Order()
                .Select(u => UsingDirective(ParseName(u)));

            var usings = globalUsings
                .Concat(localUsings)
                .Select(u => u.NormalizeWhitespace());

            return node.WithUsings(List(usings)).NormalizeWhitespace();
        },
        node => ((CompilationUnitSyntax)node).Usings);

}
