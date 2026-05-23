using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class LiteralCoercionTests
{
    // ── bool ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CoercesIntZeroToBoolFalse()
        => CheckCoercion(
            "class T { bool Italic { get; set; } void M() { this.Italic = 0; } }",
            "class T { bool Italic { get; set; } void M() { this.Italic = false; } }");

    [TestMethod]
    public void CoercesNegativeOneToTrue()
        => CheckCoercion(
            "class T { bool Strikethrough { get; set; } void M() { this.Strikethrough = -1; } }",
            "class T { bool Strikethrough { get; set; } void M() { this.Strikethrough = true; } }");

    [TestMethod]
    public void CoercesPositiveIntToTrue()
        => CheckCoercion(
            "class T { bool B { get; set; } void M() { this.B = 1; } }",
            "class T { bool B { get; set; } void M() { this.B = true; } }");

    [TestMethod]
    public void LeavesAlreadyFalseUnchanged()
        => CheckCoercion(
            "class T { bool B { get; set; } void M() { this.B = false; } }");

    [TestMethod]
    public void LeavesAlreadyTrueUnchanged()
        => CheckCoercion(
            "class T { bool B { get; set; } void M() { this.B = true; } }");

    // ── decimal ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void CoercesDoubleToDecimal()
        => CheckCoercion(
            "class T { decimal Size { get; set; } void M() { this.Size = 8.25; } }",
            "class T { decimal Size { get; set; } void M() { this.Size = 8.25M; } }");

    [TestMethod]
    public void CoercesIntToDecimal()
        => CheckCoercion(
            "class T { decimal Size { get; set; } void M() { this.Size = 8; } }",
            "class T { decimal Size { get; set; } void M() { this.Size = 8M; } }");

    [TestMethod]
    public void CoercesNegativeDoubleToDecimal()
        => CheckCoercion(
            "class T { decimal Size { get; set; } void M() { this.Size = -8.25; } }",
            "class T { decimal Size { get; set; } void M() { this.Size = -8.25M; } }");

    [TestMethod]
    public void LeavesAlreadyDecimalUnchanged()
        => CheckCoercion(
            "class T { decimal Size { get; set; } void M() { this.Size = 8.25M; } }");

    // ── float ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CoercesDoubleToFloat()
        => CheckCoercion(
            "class T { float Width { get; set; } void M() { this.Width = 8.25; } }",
            "class T { float Width { get; set; } void M() { this.Width = 8.25F; } }");

    [TestMethod]
    public void CoercesIntToFloat()
        => CheckCoercion(
            "class T { float Width { get; set; } void M() { this.Width = 8; } }",
            "class T { float Width { get; set; } void M() { this.Width = 8F; } }");

    [TestMethod]
    public void CoercesNegativeDoubleToFloat()
        => CheckCoercion(
            "class T { float Width { get; set; } void M() { this.Width = -8.25; } }",
            "class T { float Width { get; set; } void M() { this.Width = -8.25F; } }");

    [TestMethod]
    public void LeavesAlreadyFloatUnchanged()
        => CheckCoercion(
            "class T { float Width { get; set; } void M() { this.Width = 8.25F; } }");

    // ── int (uint overflow literals) ──────────────────────────────────────────

    [TestMethod]
    public void CoercesUIntHexLiteralToInt()
        => CheckCoercion(
            "class T { int Flags { get; set; } void M() { this.Flags = 0x80000010; } }",
            "class T { int Flags { get; set; } void M() { this.Flags = -2147483632; } }");

    [TestMethod]
    public void CoercesUIntHexLiteralMinValueToIntMinValue()
        => CheckCoercion(
            "class T { int Flags { get; set; } void M() { this.Flags = 0x80000000; } }",
            "class T { int Flags { get; set; } void M() { this.Flags = int.MinValue; } }");

    [TestMethod]
    public void CoercesUIntMaxToInt()
        => CheckCoercion(
            "class T { int Flags { get; set; } void M() { this.Flags = 0xFFFFFFFF; } }",
            "class T { int Flags { get; set; } void M() { this.Flags = -1; } }");

    [TestMethod]
    public void LeavesNonOverflowingHexIntUnchanged()
        => CheckCoercion(
            "class T { int Flags { get; set; } void M() { this.Flags = 0x7FFFFFFF; } }");

    // ── uint (negative int literals) ─────────────────────────────────────────

    [TestMethod]
    public void CoercesNegativeIntToUInt()
        => CheckCoercion(
            "class T { uint Flags { get; set; } void M() { this.Flags = -2147483632; } }",
            "class T { uint Flags { get; set; } void M() { this.Flags = 2147483664; } }");

    [TestMethod]
    public void CoercesNegativeOneToUIntMax()
        => CheckCoercion(
            "class T { uint Flags { get; set; } void M() { this.Flags = -1; } }",
            "class T { uint Flags { get; set; } void M() { this.Flags = 4294967295; } }");

    [TestMethod]
    public void CoercesIntMinValueToUInt()
        => CheckCoercion(
            "class T { uint Flags { get; set; } void M() { this.Flags = -2147483648; } }",
            "class T { uint Flags { get; set; } void M() { this.Flags = 2147483648; } }");

    [TestMethod]
    public void LeavesPositiveIntAssignedToUIntUnchanged()
        => CheckCoercion(
            "class T { uint Flags { get; set; } void M() { this.Flags = 42; } }");

    // ── enum ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CoercesIntToEnumMember()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = 1; } }",
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = E.On; } }");

    [TestMethod]
    public void CoercesZeroToEnumMember()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = 0; } }",
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = E.Off; } }");

    [TestMethod]
    public void CoercesHexLiteralToEnumMember()
        => CheckCoercion(
            "enum F { None = 0, Bold = 1, Italic = 2 } class T { F Style { get; set; } void M() { this.Style = 0x0002; } }",
            "enum F { None = 0, Bold = 1, Italic = 2 } class T { F Style { get; set; } void M() { this.Style = F.Italic; } }");

    [TestMethod]
    public void CastsUnmatchedLiteralToEnumType()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = 99; } }",
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = (E)99; } }");

    [TestMethod]
    public void CoercesNegativeEnumMember()
        => CheckCoercion(
            "enum E { None = 0, Error = -1 } class T { E Status { get; set; } void M() { this.Status = -1; } }",
            "enum E { None = 0, Error = -1 } class T { E Status { get; set; } void M() { this.Status = E.Error; } }");

    [TestMethod]
    public void LeavesAlreadyEnumMemberUnchanged()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { E Mode { get; set; } void M() { this.Mode = E.On; } }");

    [TestMethod]
    public void UsesDuplicateMemberFirstInDeclarationOrder()
        => CheckCoercion(
            "enum E { A = 0, B = 0, C = 1 } class T { E X { get; set; } void M() { this.X = 0; } }",
            "enum E { A = 0, B = 0, C = 1 } class T { E X { get; set; } void M() { this.X = E.A; } }");

    // ── parameter defaults ───────────────────────────────────────────────────

    [TestMethod]
    public void CoercesBoolParameterDefaultNegativeOneToTrue()
        => CheckCoercion(
            "class T { void M(bool b = -1) { } }",
            "class T { void M(bool b = true) { } }");

    [TestMethod]
    public void CoercesBoolParameterDefaultZeroToFalse()
        => CheckCoercion(
            "class T { void M(bool b = 0) { } }",
            "class T { void M(bool b = false) { } }");

    [TestMethod]
    public void CoercesDecimalParameterDefault()
        => CheckCoercion(
            "class T { void M(decimal d = 8.25) { } }",
            "class T { void M(decimal d = 8.25M) { } }");

    [TestMethod]
    public void CoercesFloatParameterDefault()
        => CheckCoercion(
            "class T { void M(float f = 8.25) { } }",
            "class T { void M(float f = 8.25F) { } }");

    [TestMethod]
    public void CoercesIntParameterDefaultUIntOverflow()
        => CheckCoercion(
            "class T { void M(int x = 0x80000010) { } }",
            "class T { void M(int x = -2147483632) { } }");

    [TestMethod]
    public void CoercesUIntParameterDefaultNegative()
        => CheckCoercion(
            "class T { void M(uint x = -1) { } }",
            "class T { void M(uint x = 4294967295) { } }");

    [TestMethod]
    public void CoercesEnumParameterDefault()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { void M(E e = 1) { } }",
            "enum E { Off = 0, On = 1 } class T { void M(E e = E.On) { } }");

    [TestMethod]
    public void LeavesAlreadyCorrectBoolParameterDefaultUnchanged()
        => CheckCoercion(
            "class T { void M(bool b = true) { } }");

    [TestMethod]
    public void LeavesAlreadyCorrectDecimalParameterDefaultUnchanged()
        => CheckCoercion(
            "class T { void M(decimal d = 8.25M) { } }");

    // ── untyped / other types unchanged ──────────────────────────────────────

    [TestMethod]
    public void LeavesIntPropertyUnchanged()
        => CheckCoercion(
            "class T { int Count { get; set; } void M() { this.Count = 0; } }");

    [TestMethod]
    public void LeavesDoublePropertyUnchanged()
        => CheckCoercion(
            "class T { double D { get; set; } void M() { this.D = 8.25; } }");

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses <paramref name="cs"/>, runs <see cref="LiteralCoercionRewriter"/>, and asserts the
    /// output equals <paramref name="expected"/>. When <paramref name="expected"/> is omitted the
    /// input is used (unchanged-input assertion).
    /// </summary>
    private static void CheckCoercion(string cs, string? expected = null)
    {
        var cu   = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter  = new LiteralCoercionRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
