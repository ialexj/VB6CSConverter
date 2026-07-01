using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ForEachVariableRewriterTests
{
    [TestMethod]
    public void MovesTypeAndRemovesDeclarationInSameBlock()
        => Check(
            "class T { void M() { CommandButton objBotao = default; foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } }",
            "class T { void M() { foreach (CommandButton objBotao in cmdFicha) { UseIt(objBotao); } } }");

    [TestMethod]
    public void MovesTypeAndRemovesDeclarationAcrossNestedBlock()
        => Check(
            "class T { void M() { CommandButton objBotao = default; { foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } } }",
            "class T { void M() { { foreach (CommandButton objBotao in cmdFicha) { UseIt(objBotao); } } } }");

    [TestMethod]
    public void KeepsVarForObjectType()
        => Check(
            "class T { void M() { object objBotao = default; foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } }",
            "class T { void M() { foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } }");

    [TestMethod]
    public void KeepsVarForDynamicType()
        => Check(
            "class T { void M() { dynamic objBotao = default; foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } }",
            "class T { void M() { foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } }");

    [TestMethod]
    public void DoesNotRemoveDeclarationUsedAfterLoop()
        => Check("class T { void M() { CommandButton objBotao = default; foreach (var objBotao in cmdFicha) { UseIt(objBotao); } UseIt(objBotao); } }");

    [TestMethod]
    public void SplitsMultiDeclaratorAcrossNestedBlock()
        => Check(
            "class T { void M() { CommandButton objBotao = default, other = default; { foreach (var objBotao in cmdFicha) { UseIt(objBotao); } } } }",
            "class T { void M() { CommandButton other = default; { foreach (CommandButton objBotao in cmdFicha) { UseIt(objBotao); } } } }");

    [TestMethod]
    public void DoesNotRewriteWhenTwoLoopsShareTheSameDeclaration()
        => Check("class T { void M() { CommandButton objBotao = default; foreach (var objBotao in cmdFicha) { UseIt(objBotao); } foreach (var objBotao in outraLista) { UseIt(objBotao); } } }");

    private static void Check(string cs, string? expected = null)
    {
        var normalized = ConvergeWithFreshSemantics(cs);
        var expectedNormalized = SyntaxFactory.ParseCompilationUnit(expected ?? cs).NormalizeWhitespace().ToFullString();
        normalized.Should().Be(expectedNormalized);
    }

    private static string ConvergeWithFreshSemantics(string cs)
    {
        string current = cs;
        for (int i = 0; i < 20; i++) {
            var next = RewriteWithFreshSemantics(current);
            if (next == current) {
                return current.Trim();
            }

            current = next;
        }

        return current.Trim();
    }

    private static string RewriteWithFreshSemantics(string cs)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location)
            ]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new ForEachVariableRewriter(semantics);

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu)!;
        return rewritten.NormalizeWhitespace().ToFullString();
    }
}
