using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
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

        IEnumerable<UsingDirectiveSyntax> GetGlobalStaticUsings()
            => @class.Members.OfType<EnumDeclarationSyntax>()
                .Select(e => UsingDirective(
                    // TODO: full name
                    IdentifierName(e.Identifier))
                    .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
                    .WithStaticKeyword(Token(SyntaxKind.StaticKeyword))
                );

        IEnumerable<string> GetUsings()
        {
            yield return "System";
            foreach (var usingDirective in @class.GetAnnotatedNodesAndTokens("Using").SelectMany(t => t.GetAnnotations("Using"))) {
                yield return usingDirective.Data;
            }
        }

        return CompilationUnit()
            .AddUsings([.. GetGlobalStaticUsings()])
            .AddUsings([.. GetUsings().Distinct().Order().Select(u => UsingDirective(ParseName(u)))])
            .WithMembers(SingletonList<MemberDeclarationSyntax>(@namespace))
            .NormalizeWhitespace();
    }

    public static CompilationUnitSyntax GetGlobalStaticUsings(IEnumerable<string> names)
    {
        var common = new string[] {
            "Microsoft.VisualBasic.FileSystem",
            "Microsoft.VisualBasic.Strings",
            "Microsoft.VisualBasic.VBMath",
            "Microsoft.VisualBasic.Constants",
            "Microsoft.VisualBasic.ControlChars",
            "Microsoft.VisualBasic.Conversion",
            "Microsoft.VisualBasic.DateAndTime",
            "Microsoft.VisualBasic.Interaction"
        };

        var usings = common.Concat(names).Select(n => UsingDirective(ParseTypeName(n))
            .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
            .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

        return CompilationUnit([], List(usings), [], []);
    }
}
