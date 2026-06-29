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

    [TestMethod]
    public void CoercesWrongEnumMemberToTargetEnumAssignment()
        => CheckCoercion(
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { E1 Mode { get; set; } void M() { this.Mode = E2.ValueB; } }",
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { E1 Mode { get; set; } void M() { this.Mode = E1.ValueB; } }");

    [TestMethod]
    public void CoercesWrongEnumMemberToTargetEnumArgument()
        => CheckCoercion(
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { void Take(E1 e) { } void M() { Take(E2.ValueB); } }",
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { void Take(E1 e) { } void M() { Take(E1.ValueB); } }");

    [TestMethod]
    public void CoercesWrongEnumMemberToTargetEnumReturn()
        => CheckCoercion(
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { E1 M() { return E2.ValueB; } }",
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { E1 M() { return E1.ValueB; } }");

    [TestMethod]
    public void CoercesWrongEnumMemberCaseInsensitive()
        => CheckCoercion(
            "enum E1 { ValueA, ValueB } enum E2 { VALUEB, ValueC } class T { E1 Mode { get; set; } void M() { this.Mode = E2.VALUEB; } }",
            "enum E1 { ValueA, ValueB } enum E2 { VALUEB, ValueC } class T { E1 Mode { get; set; } void M() { this.Mode = E1.ValueB; } }");

    [TestMethod]
    public void LeavesWrongEnumMemberUnchangedWhenNameMissingInTarget()
        => CheckCoercion(
            "enum E1 { ValueA, ValueB } enum E2 { ValueB, ValueC } class T { E1 Mode { get; set; } void M() { this.Mode = E2.ValueC; } }");

    [TestMethod]
    public void LeavesCorrectEnumMemberAccessUnchanged()
        => CheckCoercion(
            "enum E1 { ValueA, ValueB } class T { E1 Mode { get; set; } void M() { this.Mode = E1.ValueB; } }");

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

    // ── variable initializers ────────────────────────────────────────────────

    [TestMethod]
    public void CoercesDecimalLocalInitializer()
        => CheckCoercion(
            "class T { void M() { decimal d = 8.25; } }",
            "class T { void M() { decimal d = 8.25M; } }");

    [TestMethod]
    public void CoercesBoolLocalInitializer()
        => CheckCoercion(
            "class T { void M() { bool b = -1; } }",
            "class T { void M() { bool b = true; } }");

    [TestMethod]
    public void CoercesDecimalFieldInitializer()
        => CheckCoercion(
            "class T { decimal _d = 8.25; }",
            "class T { decimal _d = 8.25M; }");

    [TestMethod]
    public void CoercesEnumLocalInitializer()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { void M() { E e = 1; } }",
            "enum E { Off = 0, On = 1 } class T { void M() { E e = E.On; } }");

    [TestMethod]
    public void LeavesAlreadyCorrectLocalInitializerUnchanged()
        => CheckCoercion(
            "class T { void M() { decimal d = 8.25M; } }");

    // ── return statements ─────────────────────────────────────────────────────

    [TestMethod]
    public void CoercesDecimalReturnStatement()
        => CheckCoercion(
            "class T { decimal M() { return 8.25; } }",
            "class T { decimal M() { return 8.25M; } }");

    [TestMethod]
    public void CoercesBoolReturnStatement()
        => CheckCoercion(
            "class T { bool M() { return -1; } }",
            "class T { bool M() { return true; } }");

    [TestMethod]
    public void CoercesFloatReturnStatement()
        => CheckCoercion(
            "class T { float M() { return 8.25; } }",
            "class T { float M() { return 8.25F; } }");

    [TestMethod]
    public void CoercesEnumReturnStatement()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { E M() { return 1; } }",
            "enum E { Off = 0, On = 1 } class T { E M() { return E.On; } }");

    [TestMethod]
    public void LeavesAlreadyCorrectReturnStatementUnchanged()
        => CheckCoercion(
            "class T { decimal M() { return 8.25M; } }");

    // ── arguments ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void CoercesDecimalArgument()
        => CheckCoercion(
            "class T { void Take(decimal d) { } void M() { Take(8.25); } }",
            "class T { void Take(decimal d) { } void M() { Take(8.25M); } }");

    [TestMethod]
    public void CoercesBoolArgument()
        => CheckCoercion(
            "class T { void Take(bool b) { } void M() { Take(-1); } }",
            "class T { void Take(bool b) { } void M() { Take(true); } }");

    [TestMethod]
    public void CoercesFloatArgument()
        => CheckCoercion(
            "class T { void Take(float f) { } void M() { Take(8.25); } }",
            "class T { void Take(float f) { } void M() { Take(8.25F); } }");

    [TestMethod]
    public void CoercesEnumArgument()
        => CheckCoercion(
            "enum E { Off = 0, On = 1 } class T { void Take(E e) { } void M() { Take(1); } }",
            "enum E { Off = 0, On = 1 } class T { void Take(E e) { } void M() { Take(E.On); } }");

    [TestMethod]
    public void CoercesNamedArgument()
        => CheckCoercion(
            "class T { void Take(decimal d) { } void M() { Take(d: 8.25); } }",
            "class T { void Take(decimal d) { } void M() { Take(d: 8.25M); } }");

    [TestMethod]
    public void LeavesAlreadyCorrectArgumentUnchanged()
        => CheckCoercion(
            "class T { void Take(decimal d) { } void M() { Take(8.25M); } }");

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
