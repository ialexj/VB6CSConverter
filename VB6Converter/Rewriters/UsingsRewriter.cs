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
public class UsingsRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var usings = GetUsings(node)
            .Concat(node.Usings.Select(s => s.Name.ToString()))
            .Distinct()
            .Order()
            .Select(u => UsingDirective(ParseName(u)).NormalizeWhitespace())
            .ToArray();

        if (usings.Length > 0) {
            Log.ForTree(node.SyntaxTree).ForContext("count", usings.Length).Debug("{file}: {count} usings.");
            return node.WithUsings(List(usings)).NormalizeWhitespace();
        }
        else {
            return base.VisitCompilationUnit(node);
        }
    }

    static IEnumerable<string> GetUsings(SyntaxNode node)
    {
        yield return "System";

        foreach (var usingDirective in node.GetAnnotatedNodesAndTokens("Using").SelectMany(t => t.GetAnnotations("Using"))) {
            yield return usingDirective.Data;
        }
    }
}
