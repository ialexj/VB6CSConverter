using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class CompilationUnitConverter
{
    public static CompilationUnitSyntax GetCompilationUnit(ModuleContext module, string nsName, string className, bool isStatic)
    {
        using var _ = new TraceMethod(module);

        var @class = ClassConverter.GetClass(module, new ClassContext(className, isStatic));

        var @namespace = FileScopedNamespaceDeclaration(IdentifierName(nsName ?? className))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(@class));

        IEnumerable<string> GetUsings()
        {
            yield return "System";
            foreach (var usingDirective in @class.GetAnnotatedNodesAndTokens("Using").SelectMany(t => t.GetAnnotations("Using"))) {
                yield return usingDirective.Data;
            }
        }

        return CompilationUnit()            
            .AddUsings([.. GetUsings().Distinct().Order().Select(u => UsingDirective(ParseName(u)))])
            .WithMembers(SingletonList<MemberDeclarationSyntax>(@namespace))
            .NormalizeWhitespace();
    }

    public static CompilationUnitSyntax GetGlobalUsings(IEnumerable<string> names)
    {
        var usings = names.Select(n => UsingDirective(ParseTypeName(n))
            .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
            .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

        return CompilationUnit([], List(usings), [], []);
    }
}
