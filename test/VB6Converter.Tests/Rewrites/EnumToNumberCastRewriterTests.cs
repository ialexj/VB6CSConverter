using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class EnumToNumberCastRewriterTests
{
    [TestMethod]
    public void CastsEnumToShortInComparisonAndAssignment()
        => CheckRewrites(
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { short Value { get; set; } void M() { if (this.Value == CheckState.Checked) { this.Value = CheckState.Unchecked; } } }",
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { short Value { get; set; } void M() { if (this.Value == (short)CheckState.Checked) { this.Value = (short)CheckState.Unchecked; } } }");

    [TestMethod]
    public void CastsEnumToIntInSwitchCaseLabel()
        => CheckRewrites(
            "enum Keys : int { Back = 8 } class T { void M(int keyAscii) { switch (keyAscii) { case Keys.Back: break; } } }",
            "enum Keys : int { Back = 8 } class T { void M(int keyAscii) { switch (keyAscii) { case (int)Keys.Back: break; } } }");

    [TestMethod]
    public void CastsEnumToNumericArgument()
        => CheckRewrites(
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { void Take(short value) { } void M() { Take(CheckState.Checked); } }",
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { void Take(short value) { } void M() { Take((short)CheckState.Checked); } }");

    [TestMethod]
    public void CastsEnumToNumericReturnType()
        => CheckRewrites(
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { short M() { return CheckState.Checked; } }",
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { short M() { return (short)CheckState.Checked; } }");

    [TestMethod]
    public void CastsEnumInNumericInitializers()
        => CheckRewrites(
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { short field = CheckState.Checked; void M() { short local = CheckState.Unchecked; } }",
            "enum CheckState : short { Unchecked = 0, Checked = 1 } class T { short field = (short)CheckState.Checked; void M() { short local = (short)CheckState.Unchecked; } }");

    [TestMethod]
    public void CastsEnumToNullableNumericType()
        => CheckRewrites(
            "enum Keys : int { Back = 8 } class T { int? Value { get; set; } void M() { this.Value = Keys.Back; } }",
            "enum Keys : int { Back = 8 } class T { int? Value { get; set; } void M() { this.Value = (int?)Keys.Back; } }");

    [TestMethod]
    public void LeavesExplicitCastsUnchanged()
        => CheckRewrites(
            "enum Keys : int { Back = 8 } class T { int Value { get; set; } void M() { this.Value = (int)Keys.Back; } }");

    [TestMethod]
    public void LeavesNumericToEnumUnchanged()
        => CheckRewrites(
            "enum Keys : int { Back = 8 } class T { Keys Value { get; set; } void M() { this.Value = 8; } }");

    [TestMethod]
    public void LeavesEnumToEnumUnchanged()
        => CheckRewrites(
            "enum A : int { One = 1 } enum B : int { One = 1 } class T { void M(A a, B b) { var x = a == (A)1; var y = b; } }");

    private static void CheckRewrites(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new EnumToNumberCastRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
