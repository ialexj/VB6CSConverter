using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class DateTimeArithmeticRewriterTests
{
    [TestMethod]
    public void RewritesDateTimePlusIntToAddDays()
        => Check(
            "class T { void M() { var dt = System.DateTime.Now + 1; } }",
            "class T { void M() { var dt = System.DateTime.Now.AddDays(1); } }");

    [TestMethod]
    public void RewritesDateTimeMinusIntToAddDaysWithNegativeOffset()
        => Check(
            "class T { void M() { var dt = System.DateTime.Now - 1; } }",
            "class T { void M() { var dt = System.DateTime.Now.AddDays(-(1)); } }");

    [TestMethod]
    public void RewritesDateTimePlusDoubleToAddDays()
        => Check(
            "class T { void M() { var dt = System.DateTime.Now + 0.5; } }",
            "class T { void M() { var dt = System.DateTime.Now.AddDays(0.5); } }");

    [TestMethod]
    public void LeavesDateTimeMinusDateTimeUnchanged()
        => Check("class T { void M(System.DateTime a, System.DateTime b) { var delta = a - b; } }");

    [TestMethod]
    public void RewritesChainedDateTimeArithmetic()
        => Check(
            "class T { void M() { var dt = System.DateTime.Now + 1 - 2; } }",
            "class T { void M() { var dt = System.DateTime.Now.AddDays(1).AddDays(-(2)); } }");

    [TestMethod]
    public void LeavesNonDateTimeAdditionUnchanged()
        => Check("class T { void M(int a, int b) { var x = a + b; } }");

    private static void Check(string cs, string? expected = null)
    {
        var rewritten = RewriteWithFreshSemantics(cs);
        rewritten = RewriteWithFreshSemantics(rewritten);
        rewritten = RewriteWithFreshSemantics(rewritten);

        var expectedText = SyntaxFactory.ParseCompilationUnit(expected ?? cs).NormalizeWhitespace().ToFullString();
        var actualText = SyntaxFactory.ParseCompilationUnit(rewritten).NormalizeWhitespace().ToFullString();
        actualText.Should().Be(expectedText);
    }

    private static string RewriteWithFreshSemantics(string cs)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new DateTimeArithmeticRewriter(semantics);

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu);
        return rewritten.ToFullString();
    }
}
