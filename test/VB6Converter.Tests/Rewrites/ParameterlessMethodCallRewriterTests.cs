using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ParameterlessMethodCallRewriterTests
{
    [TestMethod]
    public void Rewrites_BareParameterlessMethodMemberAccess_To_Invocation()
        => CheckRewrite(
            "class XArr { public int Count() => 0; } class T { XArr obj = new(); bool M() { return obj.Count == 0; } }",
            "class XArr { public int Count() => 0; } class T { XArr obj = new(); bool M() { return obj.Count() == 0; } }");

    [TestMethod]
    public void Rewrites_CaseInsensitiveParameterlessMethodMemberAccess_After_MemberFinder()
        => CheckPipeline(
            "class XArr { public int Count() => 0; } class T { XArr obj = new(); bool M() { return obj.count == 0; } }",
            "class XArr { public int Count() => 0; } class T { XArr obj = new(); bool M() { return obj.Count() == 0; } }");

    [TestMethod]
    public void IsIdempotent_For_ParameterlessMethodMemberAccess_Rewrite()
        => CheckPipelineTwice(
            "class XArr { public int Hide() => 0; } class T { XArr autos = new(); void M() { _ = autos.Hide; } }",
            "class XArr { public int Hide() => 0; } class T { XArr autos = new(); void M() { _ = autos.Hide(); } }");

    private static void CheckRewrite(string cs, string expected)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new ParameterlessMethodCallRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        var actual = CSharpSyntaxTree.ParseText(newCu!.ToFullString()).GetRoot().NormalizeWhitespace().ToFullString();
        var expectedText = CSharpSyntaxTree.ParseText(expected).GetRoot().NormalizeWhitespace().ToFullString();
        actual.Should().Be(expectedText);
    }

    private static void CheckPipeline(string cs, string expected)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var memberFinder = new MemberFinder(comp.GetSemanticModel(cu.SyntaxTree, true));
        var memberFixed = (CompilationUnitSyntax)memberFinder.Visit(cu)!;

        var updatedComp = CSharpCompilation.Create("Test2",
            [memberFixed.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var callRewriter = new ParameterlessMethodCallRewriter(updatedComp.GetSemanticModel(memberFixed.SyntaxTree, true));
        var rewritten = callRewriter.Visit(memberFixed);

        var actual = CSharpSyntaxTree.ParseText(rewritten!.ToFullString()).GetRoot().NormalizeWhitespace().ToFullString();
        var expectedText = CSharpSyntaxTree.ParseText(expected).GetRoot().NormalizeWhitespace().ToFullString();
        actual.Should().Be(expectedText);
    }

    private static void CheckPipelineTwice(string cs, string expected)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var firstMemberFinder = new MemberFinder(comp.GetSemanticModel(cu.SyntaxTree, true));
        var memberFixed = (CompilationUnitSyntax)firstMemberFinder.Visit(cu)!;

        var secondComp = CSharpCompilation.Create("Test2",
            [memberFixed.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var firstCallPass = new ParameterlessMethodCallRewriter(secondComp.GetSemanticModel(memberFixed.SyntaxTree, true));
        var once = (CompilationUnitSyntax)firstCallPass.Visit(memberFixed)!;

        var thirdComp = CSharpCompilation.Create("Test3",
            [once.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var secondCallPass = new ParameterlessMethodCallRewriter(thirdComp.GetSemanticModel(once.SyntaxTree, true));
        var twice = secondCallPass.Visit(once);

        var actual = CSharpSyntaxTree.ParseText(twice!.ToFullString()).GetRoot().NormalizeWhitespace().ToFullString();
        var expectedText = CSharpSyntaxTree.ParseText(expected).GetRoot().NormalizeWhitespace().ToFullString();
        actual.Should().Be(expectedText);
    }
}