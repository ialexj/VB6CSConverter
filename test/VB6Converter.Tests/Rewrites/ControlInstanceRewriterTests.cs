using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ControlInstanceRewriterTests
{
    [TestMethod]
    public void RewritesLoadArgumentToControlInstance()
        => CheckRewrites(
            "class T { void M() { Load(Form1); } }",
            "class T { void M() { Load(Form1._Instance); } }");

    [TestMethod]
    public void RewritesUnloadArgumentToControlInstance()
        => CheckRewrites(
            "class T { void M() { Unload(Form1); } }",
            "class T { void M() { Unload(Form1._Instance); } }");

    [TestMethod]
    public void LeavesLoadArgumentUnchangedForNonControl()
        => CheckRewrites(
            "class T { void M() { Load(value); } }");

    [TestMethod]
    public void DoesNotDoubleRewriteExistingInstance()
        => CheckRewrites(
            "class T { void M() { Load(Form1._Instance); Unload(Form1._Instance); } }");

    private static void CheckRewrites(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var rewriter = new ControlInstanceRewriter(["Form1"], "CurrentForm");

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu);
        rewritten.ToFullString().Should().Be(expected ?? cs);
    }
}
