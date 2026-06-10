using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class VBLiteralRewriterTests
{
    // ── String / char ─────────────────────────────────────────────────────────

    [TestMethod]
    public void NullString() => ValidateBodyMatches(
        "x = vbNullString",
        "x = \"\";");

    [TestMethod]
    public void CrLf() => ValidateBodyMatches(
        "x = vbCrLf",
        """x = "\r\n";""");

    [TestMethod]
    public void Tab() => ValidateBodyMatches(
        "x = vbTab",
        "x = '\\t';");

    // ── Day of week — FirstDayOfWeek enum ────────────────────────────────────

    [TestMethod]
    public void Sunday() => ValidateBodyMatches(
        "x = vbSunday",
        "x = Microsoft.VisualBasic.FirstDayOfWeek.Sunday;");

    [TestMethod]
    public void Saturday() => ValidateBodyMatches(
        "x = vbSaturday",
        "x = Microsoft.VisualBasic.FirstDayOfWeek.Saturday;");

    // ── Tristate — TriState enum ──────────────────────────────────────────────

    [TestMethod]
    public void TriStateTrue() => ValidateBodyMatches(
        "x = vbTrue",
        "x = Microsoft.VisualBasic.TriState.True;");

    [TestMethod]
    public void TriStateFalse() => ValidateBodyMatches(
        "x = vbFalse",
        "x = Microsoft.VisualBasic.TriState.False;");

    [TestMethod]
    public void TriStateUseDefault() => ValidateBodyMatches(
        "x = vbUseDefault",
        "x = Microsoft.VisualBasic.TriState.UseDefault;");

    // ── Compare — CompareMethod enum ─────────────────────────────────────────

    [TestMethod]
    public void BinaryCompare() => ValidateBodyMatches(
        "x = vbBinaryCompare",
        "x = Microsoft.VisualBasic.CompareMethod.Binary;");

    [TestMethod]
    public void TextCompare() => ValidateBodyMatches(
        "x = vbTextCompare",
        "x = Microsoft.VisualBasic.CompareMethod.Text;");

    // ── StrConv — VbStrConv enum ──────────────────────────────────────────────

    [TestMethod]
    public void UpperCase() => ValidateBodyMatches(
        "x = vbUpperCase",
        "x = Microsoft.VisualBasic.VbStrConv.UpperCase;");

    [TestMethod]
    public void LowerCase() => ValidateBodyMatches(
        "x = vbLowerCase",
        "x = Microsoft.VisualBasic.VbStrConv.LowerCase;");

    // ── Date format — DateFormat enum ─────────────────────────────────────────

    [TestMethod]
    public void LongDate() => ValidateBodyMatches(
        "x = vbLongDate",
        "x = Microsoft.VisualBasic.DateFormat.LongDate;");

    [TestMethod]
    public void ShortDate() => ValidateBodyMatches(
        "x = vbShortDate",
        "x = Microsoft.VisualBasic.DateFormat.ShortDate;");

    // ── File attributes — FileAttribute enum ─────────────────────────────────

    [TestMethod]
    public void ReadOnly() => ValidateBodyMatches(
        "x = vbReadOnly",
        "x = Microsoft.VisualBasic.FileAttribute.ReadOnly;");

    [TestMethod]
    public void Hidden() => ValidateBodyMatches(
        "x = vbHidden",
        "x = Microsoft.VisualBasic.FileAttribute.Hidden;");

    [TestMethod]
    public void Archive() => ValidateBodyMatches(
        "x = vbArchive",
        "x = Microsoft.VisualBasic.FileAttribute.Archive;");

    // ── Shell window style — AppWinStyle enum ────────────────────────────────

    [TestMethod]
    public void Hide() => ValidateBodyMatches(
        "x = vbHide",
        "x = Microsoft.VisualBasic.AppWinStyle.Hide;");

    [TestMethod]
    public void NormalFocus() => ValidateBodyMatches(
        "x = vbNormalFocus",
        "x = Microsoft.VisualBasic.AppWinStyle.NormalFocus;");

    // ── VarType — integer literals (VariantType enum names differ from VB6) ──

    [TestMethod]
    public void VarTypeInteger() => ValidateBodyMatches(
        "x = vbInteger",
        "x = 2 /* vbInteger */;");

    [TestMethod]
    public void VarTypeString() => ValidateBodyMatches(
        "x = vbString",
        "x = 8 /* vbString */;");

    [TestMethod]
    public void VarTypeArray() => ValidateBodyMatches(
        "x = vbArray",
        "x = 8192 /* vbArray */;");
}
