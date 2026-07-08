using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using VB6Converter.Rewriters;
using VB6Converter.Rewriters.Forms;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

public static class CompilationUnitConverter
{
    static CSharpSyntaxRewriter[] CreateRewriters(string file) => [
        new VBLiteralRewriter(file),
        new VBCoreRewriter(file),

        new CursorRewriter(file),

        new ErrRaiseRewriter(file),
        new ErrObjectRewriter(file),

        new KeywordEscapeRewriter(file)
    ];

    public static CompilationUnitSyntax GetCompilationUnit(ModuleContext module, string nsName, string className, bool isStatic, ConversionOptions options = null, string sourceDirectory = null, string outputDirectory = null, string sourceRelativePath = null)
    {
        using var _ = new TraceMethod(module);

        var classNs = ParseName(nsName ?? className);
        var classDef = ClassConverter.GetClass(module, new ClassContext(className, isStatic, options ?? ConversionOptions.Default, sourceDirectory, outputDirectory, sourceRelativePath));

        var cid = IdentifierName(classDef.Identifier);
        NameSyntax classFullName = classNs != null ? QualifiedName(classNs, cid) : cid;

        IEnumerable<NameSyntax> GetClassGlobalStaticUsings() {
            yield return classFullName;

            // Expose the enums as global static usings
            var enums = classDef.DescendantNodes().OfType<EnumDeclarationSyntax>()
                .Select(e => QualifiedName(classFullName, IdentifierName(e.Identifier)));

            foreach (var e in enums) {
                yield return e;
            }
        }

        var usings = GetClassGlobalStaticUsings()
            .Select(n => UsingDirective(n)
                .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
                .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

        var ns = FileScopedNamespaceDeclaration(classNs)
            .WithMembers(SingletonList<MemberDeclarationSyntax>(classDef));

        var cu = CompilationUnit(default, List(usings), default, SingletonList<MemberDeclarationSyntax>(ns));

        foreach (var rewriter in CreateRewriters(sourceRelativePath)) {
            cu = (CompilationUnitSyntax)rewriter.Visit(cu);
        }

        return cu.NormalizeWhitespace();
    }

    public static CompilationUnitSyntax GetGlobalStaticUsings()
    {
        var common = new string[] {
            "VB6",
        };

        var usings = common
            .Select(n => UsingDirective(ParseTypeName(n))
            .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
            .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

        var vb6Class = ClassDeclaration("VB6")
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
            .WithMembers(List<MemberDeclarationSyntax>([]));

        return CompilationUnit([], List(usings), [], SingletonList<MemberDeclarationSyntax>(vb6Class));
    }


}
