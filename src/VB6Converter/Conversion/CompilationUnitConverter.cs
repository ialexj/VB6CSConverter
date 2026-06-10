using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using VB6Converter.Rewriters;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

public static class CompilationUnitConverter
{
    static readonly CSharpSyntaxRewriter[] rewriters = [
        new VBLiteralRewriter(),
        new VBCoreRewriter(),

        //new CursorRewriter(),
        //new KeysRewriter(),
        //new MsgBoxRewriter(),
        //new CheckStateRewriter(),
        new KeywordEscapeRewriter(),

        UsingsRewriter.Default
    ];

    public static CompilationUnitSyntax GetCompilationUnit(ModuleContext module, string nsName, string className, bool isStatic, ConversionOptions options = null, string sourceDirectory = null, string outputDirectory = null, string sourceRelativePath = null)
    {
        using var _ = new TraceMethod(module);

        var namespaceName = ParseName(nsName ?? className);

        var @class = ClassConverter.GetClass(module, new ClassContext(className, isStatic, options ?? ConversionOptions.Default, sourceDirectory, outputDirectory, sourceRelativePath));

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
            "Microsoft.VisualBasic.Interaction",
            "Microsoft.VisualBasic.Information",
            "VB6",
        };

        var usings = common
            .Select(n => UsingDirective(ParseTypeName(n))
            .WithGlobalKeyword(Token(SyntaxKind.GlobalKeyword))
            .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)));

        var vb6Class = ClassDeclaration("VB6")
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
            .WithMembers(List<MemberDeclarationSyntax>([
                FieldDeclaration(
                        VariableDeclaration(ParseTypeName("Microsoft.VisualBasic.ErrObject"))
                            .WithVariables(SingletonSeparatedList(
                                VariableDeclarator("Err")
                                    .WithInitializer(EqualsValueClause(
                                        InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                ParseName("Microsoft.VisualBasic.Information"),
                                                IdentifierName("Err"))))))))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.ReadOnlyKeyword))),
                FieldDeclaration(
                        VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword)))
                            .WithVariables(SingletonSeparatedList(
                                VariableDeclarator("Erl")
                                    .WithInitializer(EqualsValueClause(
                                        InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                ParseName("Microsoft.VisualBasic.Information"),
                                                IdentifierName("Erl"))))))))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.ReadOnlyKeyword)))
            ]));

        return CompilationUnit([], List(usings), [], SingletonList<MemberDeclarationSyntax>(vb6Class));
    }
}
