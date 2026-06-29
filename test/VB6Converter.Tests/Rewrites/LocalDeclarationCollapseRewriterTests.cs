using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class LocalDeclarationCollapseRewriterTests
{
    [TestMethod]
    public void CollapsesDefaultPlusImmediateAssignment()
        => Check(
            "class T { void M(bool value) { int bgColor = default; bgColor = (int)((value ? 1 : 2)); } }",
            "class T { void M(bool value) { int bgColor = (int)((value ? 1 : 2)); } }");

    [TestMethod]
    public void MovesDeclarationIntoNestedScopeWhenUnusedOutside()
        => Check(
            "class T { void M() { int i = default; System.Console.WriteLine(1); if (true) { i = 10; } } }",
            "class T { void M() { System.Console.WriteLine(1); if (true) { int i = 10; } } }");

    [TestMethod]
    public void DoesNotMoveIntoNestedScopeWhenUsedAfter()
        => Check(
            "class T { void M() { int i = default; if (true) { i = 10; } if (i > 10) { System.Console.WriteLine(i); } } }");

    [TestMethod]
    public void SplitsMultiDeclaratorAndCollapsesEligibleVariable()
        => Check(
            "class T { void M() { int a = default, b = default; b = 2; } }",
            "class T { void M() { int a = default; int b = 2; } }");

    [TestMethod]
    public void IsIdempotentAcrossPasses()
    {
        const string source = "class T { void M(bool value) { int bgColor = default; bgColor = (int)((value ? 1 : 2)); } }";

        var pass1 = RewriteWithFreshSemantics(source);
        var pass2 = RewriteWithFreshSemantics(pass1);

        pass2.Should().Be(pass1);
    }

    [TestMethod]
    public void RecurseAfterOuterRewrite_DoesNotThrowAndConverges()
        => Check(
            "class T { void M() { int i = default; i = 1; if (true) { int j = default; j = 2; } } }",
            "class T { void M() { int i = 1; if (true) { int j = 2; } } }");

    private static void Check(string cs, string? expected = null)
    {
        // Converge like the real pipeline does — rewrite until stable.
        var normalized = ConvergeWithFreshSemantics(cs);
        var expectedNormalized = SyntaxFactory.ParseCompilationUnit(expected ?? cs).NormalizeWhitespace().ToFullString();
        normalized.Should().Be(expectedNormalized);
    }

    /// <summary>Applies the rewriter repeatedly until the output is stable (or 20 iterations).</summary>
    private static string ConvergeWithFreshSemantics(string cs)
    {
        string current = cs;
        for (int i = 0; i < 20; i++)
        {
            var next = RewriteWithFreshSemantics(current);
            if (next == current)
                return current;
            current = next;
        }
        return current;
    }

    private static string RewriteWithFreshSemantics(string cs)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new LocalDeclarationCollapseRewriter(semantics);

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu)!;
        return rewritten.NormalizeWhitespace().ToFullString();
    }
}
