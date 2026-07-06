using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class LocalDeclarationInsertionRewriterTests
{
    [TestMethod]
    public void InsertsDeclarationFromFirstAssignmentType()
        => Check(
            "class T { void M() { rsLCStock = string.Empty; } }",
            "class T { void M() { string rsLCStock = default; rsLCStock = string.Empty; } }");

    [TestMethod]
    public void InsertsOnlyOnceForMultipleAssignments()
        => Check(
            "class T { void M() { rsLCStock = 1; rsLCStock = 2; } }",
            "class T { void M() { int rsLCStock = default; rsLCStock = 1; rsLCStock = 2; } }");

    [TestMethod]
    public void FallsBackToDynamicWhenRhsTypeIsUnknown()
        => Check(
            "class T { void M() { rsLCStock = Missing(); } }",
            "class T { void M() { dynamic rsLCStock = default; rsLCStock = Missing(); } }");

    [TestMethod]
    public void SkipsWhenReadBeforeFirstAssignment()
        => Check(
            "class T { void M() { if (rsLCStock > 0) { } rsLCStock = 1; } }");

    [TestMethod]
    public void SkipsWhenTypeHierarchyHasSameNamedMember()
        => Check(
            "class B { private int rsLCStock; } class T : B { void M() { rsLCStock = 1; } }",
            "class B { private int rsLCStock; } class T : B { void M() { rsLCStock = 1; } }");

    [TestMethod]
    public void IsIdempotentAcrossPasses()
    {
        const string source = "class T { void M() { rsLCStock = string.Empty; } }";

        var pass1 = RewriteInsertionWithFreshSemantics(source);
        var pass2 = RewriteInsertionWithFreshSemantics(pass1);

        pass2.Should().Be(pass1);
    }

    [TestMethod]
    public void CooperatesWithHoistingForCrossBlockUsage()
    {
        const string source = "class T { void M() { if (true) { rsLCStock = 1; } System.Console.WriteLine(rsLCStock); } }";
        const string expected = "class T { void M() { int rsLCStock = default; if (true) { rsLCStock = 1; } System.Console.WriteLine(rsLCStock); } }";

        var normalized = RewriteInsertionThenHoist(source);
        var expectedNormalized = SyntaxFactory.ParseCompilationUnit(expected).NormalizeWhitespace().ToFullString();

        normalized.Should().Be(expectedNormalized);
    }

    private static void Check(string cs, string? expected = null)
    {
        var normalized = RewriteInsertionWithFreshSemantics(cs);
        var expectedNormalized = SyntaxFactory.ParseCompilationUnit(expected ?? cs).NormalizeWhitespace().ToFullString();
        normalized.Should().Be(expectedNormalized);
    }

    private static string RewriteInsertionWithFreshSemantics(string cs)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location)
            ]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new LocalDeclarationInsertionRewriter(semantics);

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu)!;
        return rewritten.NormalizeWhitespace().ToFullString();
    }

    private static string RewriteInsertionThenHoist(string cs)
    {
        var inserted = RewriteInsertionWithFreshSemantics(cs);

        var cu = SyntaxFactory.ParseCompilationUnit(inserted);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location)
            ]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var hoistingRewriter = new LocalDeclarationHoistingRewriter(semantics);
        var hoisted = (CompilationUnitSyntax)hoistingRewriter.Visit(cu)!;

        return hoisted.NormalizeWhitespace().ToFullString();
    }
}