using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class LocalDeclarationHoistingRewriterTests
{
    [TestMethod]
    public void HoistsDefaultDeclarationOutOfTryWhenUsedAfter()
        => Check(
            "class T { void M() { try { string something = default; } catch { goto erro; } erro: if (something == \"\") { } } }",
            "class T { void M() { string something = default; try { } catch { goto erro; } erro: if (something == \"\") { } } }");

    [TestMethod]
    public void HoistsAcrossMultipleNestedScopesUntilUsageMatches()
        => Check(
            "class T { void M() { try { if (true) { int value = default; value = 1; } } catch { } System.Console.WriteLine(value); } }",
            "class T { void M() { int value = default; try { if (true) { value = 1; } } catch { } System.Console.WriteLine(value); } }");

    [TestMethod]
    public void HoistsConstantInitializerOutOfNestedIf()
        => Check(
            "class T { void M() { if (true) { string name = \"\"; } System.Console.WriteLine(name); } }",
            "class T { void M() { string name = \"\"; if (true) { } System.Console.WriteLine(name); } }");

    [TestMethod]
    public void SplitsMultiDeclaratorBeforeHoistingEligibleVariable()
        => Check(
            "class T { void M() { if (true) { int a = default, b = default; } System.Console.WriteLine(b); } }",
            "class T { void M() { int b = default; if (true) { int a = default; } System.Console.WriteLine(b); } }");

    [TestMethod]
    public void DoesNotHoistNonConstantInitializer()
        => Check(
            "class T { void M() { if (true) { int tick = System.Environment.TickCount; } System.Console.WriteLine(tick); } }");

    [TestMethod]
    public void IsIdempotentAcrossPasses()
    {
        const string source = "class T { void M() { try { string something = default; } catch { goto erro; } erro: if (something == \"\") { } } }";

        var pass1 = RewriteWithFreshSemantics(source);
        var pass2 = RewriteWithFreshSemantics(pass1);

        pass2.Should().Be(pass1);
    }

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
        var rewriter = new LocalDeclarationHoistingRewriter(semantics);

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu)!;
        return rewritten.NormalizeWhitespace().ToFullString();
    }
}