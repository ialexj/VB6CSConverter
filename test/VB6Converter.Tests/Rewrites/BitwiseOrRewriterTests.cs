using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class BitwiseOrRewriterTests
{
    // ── Or: non-boolean operands → bitwise | ─────────────────────────────────

    [TestMethod]
    public void RewritesIntOrInt()
        => Check(
            "class T { void M(int a, int b) { int x = a || b; } }",
            "class T { void M(int a, int b) { int x = a | b; } }");

    [TestMethod]
    public void RewritesLongOrLong()
        => Check(
            "class T { void M(long a, long b) { long x = a || b; } }",
            "class T { void M(long a, long b) { long x = a | b; } }");

    [TestMethod]
    public void RewritesChainedIntOr()
        => Check(
            "class T { void M(int a, int b, int c) { int x = a || b || c; } }",
            "class T { void M(int a, int b, int c) { int x = a | b | c; } }");

    // ── Or: boolean operands → leave unchanged ────────────────────────────────

    [TestMethod]
    public void LeavesBoolOrBoolUnchanged()
        => Check("class T { void M(bool a, bool b) { bool x = a || b; } }");

    [TestMethod]
    public void LeavesBoolOrIntUnchanged()
        => Check("class T { void M(bool a, int b) { var x = a || b; } }");

    [TestMethod]
    public void LeavesIntOrBoolUnchanged()
        => Check("class T { void M(int a, bool b) { var x = a || b; } }");

    // ── And: non-boolean operands → bitwise & ────────────────────────────────

    [TestMethod]
    public void RewritesIntAndInt()
        => Check(
            "class T { void M(int a, int b) { int x = a && b; } }",
            "class T { void M(int a, int b) { int x = a & b; } }");

    [TestMethod]
    public void RewritesLongAndLong()
        => Check(
            "class T { void M(long a, long b) { long x = a && b; } }",
            "class T { void M(long a, long b) { long x = a & b; } }");

    [TestMethod]
    public void RewritesChainedIntAnd()
        => Check(
            "class T { void M(int a, int b, int c) { int x = a && b && c; } }",
            "class T { void M(int a, int b, int c) { int x = a & b & c; } }");

    // ── And: boolean operands → leave unchanged ───────────────────────────────

    [TestMethod]
    public void LeavesBoolAndBoolUnchanged()
        => Check("class T { void M(bool a, bool b) { bool x = a && b; } }");

    [TestMethod]
    public void LeavesBoolAndIntUnchanged()
        => Check("class T { void M(bool a, int b) { var x = a && b; } }");

    [TestMethod]
    public void LeavesIntAndBoolUnchanged()
        => Check("class T { void M(int a, bool b) { var x = a && b; } }");

    // ── Enum +: enum + enum → enum | enum ────────────────────────────────────

    [TestMethod]
    public void RewritesEnumPlusEnum()
        => Check(
            "enum F { A = 1, B = 2 } class T { void M() { var x = F.A + F.B; } }",
            "enum F { A = 1, B = 2 } class T { void M() { var x = F.A | F.B; } }");

    [TestMethod]
    public void RewritesChainedEnumPlusEnum()
        => Check(
            "enum F { A = 1, B = 2, C = 4 } class T { void M() { var x = F.A + F.B + F.C; } }",
            "enum F { A = 1, B = 2, C = 4 } class T { void M() { var x = F.A | F.B | F.C; } }");

    [TestMethod]
    public void LeavesEnumPlusIntUnchanged()
        => Check("enum F { A = 1 } class T { void M() { var x = F.A + 1; } }");

    [TestMethod]
    public void LeavesIntPlusEnumUnchanged()
        => Check("enum F { A = 1 } class T { void M() { var x = 1 + F.A; } }");

    [TestMethod]
    public void LeavesIntPlusIntUnchanged()
        => Check("class T { void M(int a, int b) { var x = a + b; } }");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Check(string cs, string? expected = null)
    {
        var cu   = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter  = new BitwiseOrRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
