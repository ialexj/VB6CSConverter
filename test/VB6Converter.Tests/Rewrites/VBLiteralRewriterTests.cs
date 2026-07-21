using VB6Converter.Rewriters;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class VBLiteralRewriterTests
{
    // ── String / char ─────────────────────────────────────────────────────────

    [TestMethod]
    public void NullString() => ValidateBodyMatches(
        "x = vbNullString",
        "x = \"\";", new VBLiteralRewriter());

    [TestMethod]
    public void CrLf() => ValidateBodyMatches(
        "x = vbCrLf",
        """x = "\r\n";""", new VBLiteralRewriter());

    [TestMethod]
    public void Tab() => ValidateBodyMatches(
        "x = vbTab",
        "x = '\\t';", new VBLiteralRewriter());

    // ── Day of week — FirstDayOfWeek enum ────────────────────────────────────

    [TestMethod]
    public void Sunday() => ValidateBodyMatches(
        "x = vbSunday",
        "x = Microsoft.VisualBasic.FirstDayOfWeek.Sunday;", new VBLiteralRewriter());

    [TestMethod]
    public void Saturday() => ValidateBodyMatches(
        "x = vbSaturday",
        "x = Microsoft.VisualBasic.FirstDayOfWeek.Saturday;", new VBLiteralRewriter());

    // ── Tristate — TriState enum ──────────────────────────────────────────────

    [TestMethod]
    public void TriStateTrue() => ValidateBodyMatches(
        "x = vbTrue",
        "x = Microsoft.VisualBasic.TriState.True;", new VBLiteralRewriter());

    [TestMethod]
    public void TriStateFalse() => ValidateBodyMatches(
        "x = vbFalse",
        "x = Microsoft.VisualBasic.TriState.False;", new VBLiteralRewriter());

    [TestMethod]
    public void TriStateUseDefault() => ValidateBodyMatches(
        "x = vbUseDefault",
        "x = Microsoft.VisualBasic.TriState.UseDefault;", new VBLiteralRewriter());

    // ── Compare — CompareMethod enum ─────────────────────────────────────────

    [TestMethod]
    public void BinaryCompare() => ValidateBodyMatches(
        "x = vbBinaryCompare",
        "x = Microsoft.VisualBasic.CompareMethod.Binary;", new VBLiteralRewriter());

    [TestMethod]
    public void TextCompare() => ValidateBodyMatches(
        "x = vbTextCompare",
        "x = Microsoft.VisualBasic.CompareMethod.Text;", new VBLiteralRewriter());

    // ── StrConv — VbStrConv enum ──────────────────────────────────────────────

    [TestMethod]
    public void UpperCase() => ValidateBodyMatches(
        "x = vbUpperCase",
        "x = Microsoft.VisualBasic.VbStrConv.UpperCase;", new VBLiteralRewriter());

    [TestMethod]
    public void LowerCase() => ValidateBodyMatches(
        "x = vbLowerCase",
        "x = Microsoft.VisualBasic.VbStrConv.LowerCase;", new VBLiteralRewriter());

    // ── Date format — DateFormat enum ─────────────────────────────────────────

    [TestMethod]
    public void LongDate() => ValidateBodyMatches(
        "x = vbLongDate",
        "x = Microsoft.VisualBasic.DateFormat.LongDate;", new VBLiteralRewriter());

    [TestMethod]
    public void ShortDate() => ValidateBodyMatches(
        "x = vbShortDate",
        "x = Microsoft.VisualBasic.DateFormat.ShortDate;", new VBLiteralRewriter());

    // ── File attributes — FileAttribute enum ─────────────────────────────────

    [TestMethod]
    public void ReadOnly() => ValidateBodyMatches(
        "x = vbReadOnly",
        "x = Microsoft.VisualBasic.FileAttribute.ReadOnly;", new VBLiteralRewriter());

    [TestMethod]
    public void Hidden() => ValidateBodyMatches(
        "x = vbHidden",
        "x = Microsoft.VisualBasic.FileAttribute.Hidden;", new VBLiteralRewriter());

    [TestMethod]
    public void Archive() => ValidateBodyMatches(
        "x = vbArchive",
        "x = Microsoft.VisualBasic.FileAttribute.Archive;", new VBLiteralRewriter());

    // ── Shell window style — AppWinStyle enum ────────────────────────────────

    [TestMethod]
    public void Hide() => ValidateBodyMatches(
        "x = vbHide",
        "x = Microsoft.VisualBasic.AppWinStyle.Hide;", new VBLiteralRewriter());

    [TestMethod]
    public void NormalFocus() => ValidateBodyMatches(
        "x = vbNormalFocus",
        "x = Microsoft.VisualBasic.AppWinStyle.NormalFocus;", new VBLiteralRewriter());

    // ── VarType — integer literals (VariantType enum names differ from VB6) ──

    [TestMethod]
    public void VarTypeInteger() => ValidateBodyMatches(
        "x = vbInteger",
        "x = Microsoft.VisualBasic.Constants.vbInteger;", new VBLiteralRewriter());

    [TestMethod]
    public void VarTypeString() => ValidateBodyMatches(
        "x = vbString",
        "x = Microsoft.VisualBasic.Constants.vbString;", new VBLiteralRewriter());

    [TestMethod]
    public void VarTypeArray() => ValidateBodyMatches(
        "x = vbArray",
        "x = Microsoft.VisualBasic.Constants.vbArray;", new VBLiteralRewriter());

    // ── Member access whose `.Name` collides with a constant name ───────────
    // Must NOT be treated as the VB constant, since the rewriter would produce
    // an invalid MemberAccessExpressionSyntax.Name (a SimpleNameSyntax slot).

    [TestMethod]
    public void MemberAccessNameNotRewritten() => ValidateBodyMatches(
        "x = Screen.vbNormal",
        "x = Screen.vbNormal;", new VBLiteralRewriter());
}
