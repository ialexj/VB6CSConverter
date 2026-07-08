using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class PropertyTypeRefinerTests
{
    // ── Refines get-only computed properties ─────────────────────────────────

    [TestMethod]
    public void RefinesExpressionBodiedGetterToBool()
        => Check(
            "class T { int Operacao; public dynamic Edicao { get => Operacao != 3; } }",
            "class T { int Operacao; public bool Edicao { get => Operacao != 3; } }");

    [TestMethod]
    public void RefinesObjectTypedGetterToBool()
        => Check(
            "class T { int Operacao; public object Edicao { get => Operacao != 3; } }",
            "class T { int Operacao; public bool Edicao { get => Operacao != 3; } }");

    [TestMethod]
    public void RefinesBlockBodiedGetterWithSingleReturn()
        => Check(
            "class T { public dynamic Total { get { return 42; } } }",
            "class T { public int Total { get { return 42; } } }");

    [TestMethod]
    public void RefinesBlockBodiedGetterWithAgreeingReturns()
        => Check(
            "class T { bool F; public dynamic X { get { if (F) { return 1; } return 2; } } }",
            "class T { bool F; public int X { get { if (F) { return 1; } return 2; } } }");

    // ── Leaves unchanged when refinement isn't safe ───────────────────────────

    [TestMethod]
    public void LeavesAlreadyTypedPropertyUnchanged()
        => Check("class T { public bool Edicao { get => true; } }");

    [TestMethod]
    public void LeavesPropertyWithSetterUnchanged()
        => Check("class T { public dynamic X { get => 1; set { } } }");

    [TestMethod]
    public void LeavesAmbiguousReturnTypesUnchanged()
        => Check("class T { bool F; public dynamic X { get { if (F) { return 1; } return \"a\"; } } }");

    [TestMethod]
    public void LeavesDynamicReturnExpressionUnchanged()
        => Check("class T { dynamic Y; public dynamic X { get => Y; } }");

    // ── Refines method return types with the same safety rules ──────────────

    [TestMethod]
    public void RefinesExpressionBodiedMethodToBool()
        => Check(
            "class T { int Operacao; public dynamic Edicao() => Operacao != 3; }",
            "class T { int Operacao; public bool Edicao() => Operacao != 3; }");

    [TestMethod]
    public void RefinesObjectTypedMethodToInt()
        => Check(
            "class T { public object Total() { return 42; } }",
            "class T { public int Total() { return 42; } }");

    [TestMethod]
    public void RefinesBlockBodiedMethodWithAgreeingReturns()
        => Check(
            "class T { bool F; public dynamic X() { if (F) { return 1; } return 2; } }",
            "class T { bool F; public int X() { if (F) { return 1; } return 2; } }");

    [TestMethod]
    public void LeavesMethodWithAmbiguousReturnTypesUnchanged()
        => Check("class T { bool F; public dynamic X() { if (F) { return 1; } return \"a\"; } }");

    [TestMethod]
    public void LeavesMethodWithDynamicReturnExpressionUnchanged()
        => Check("class T { dynamic Y; public dynamic X() { return Y; } }");

    [TestMethod]
    public void LeavesMethodWithoutBodyUnchanged()
        => Check("abstract class T { public abstract dynamic X(); }");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Check(string cs, string? expected = null)
    {
        var cu   = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter  = new PropertyTypeRefiner(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
