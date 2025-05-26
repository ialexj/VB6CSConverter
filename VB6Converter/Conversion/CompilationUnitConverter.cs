using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using VB6Converter.Rewriters;
using VB6Converter.Rewriters.Forms;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

public static class CompilationUnitConverter
{
    static readonly CSharpSyntaxRewriter[] rewriters = [
        new VBLiteralRewriter(),
        new VBCoreRewriter(),

        new CursorRewriter(),
        new KeysRewriter(),
        new MsgBoxRewriter(),

        UsingsRewriter.Default
    ];

    public static CompilationUnitSyntax GetCompilationUnit(ModuleContext module, string nsName, string className, bool isStatic)
    {
        using var _ = new TraceMethod(module);

        var namespaceName = ParseName(nsName ?? className);

        var @class = ClassConverter.GetClass(module, new ClassContext(className, isStatic));
        
        var @namespace = FileScopedNamespaceDeclaration(namespaceName)
            .WithMembers(SingletonList<MemberDeclarationSyntax>(@class));

        var cu = CompilationUnit(default, default, default, SingletonList<MemberDeclarationSyntax>(@namespace));

        foreach (var rewriter in rewriters) {
            cu = (CompilationUnitSyntax)rewriter.Visit(cu);
        }

        return cu.NormalizeWhitespace();
    }

    public static CompilationUnitSyntax GetGlobalStaticUsings()
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

        var usings = common
            .Select(n => UsingDirective(ParseTypeName(n))
            .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
            .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

        return CompilationUnit([], List(usings), [], []);
    }
}
