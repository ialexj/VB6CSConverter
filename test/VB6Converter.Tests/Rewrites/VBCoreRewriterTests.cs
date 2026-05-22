using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class VBCoreRewriterTests
{
    // ── Math ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Abs() => ValidateBodyMatches(
        "y = Abs(x)",
        "y = Math.Abs(x);");

    [TestMethod]
    public void Sin() => ValidateBodyMatches(
        "y = Sin(x)",
        "y = Math.Sin(x);");

    [TestMethod]
    public void Cos() => ValidateBodyMatches(
        "y = Cos(x)",
        "y = Math.Cos(x);");

    [TestMethod]
    public void Tan() => ValidateBodyMatches(
        "y = Tan(x)",
        "y = Math.Tan(x);");

    [TestMethod]
    public void Atn() => ValidateBodyMatches(
        "y = Atn(x)",
        "y = Math.Atan(x);");

    [TestMethod]
    public void Sqr() => ValidateBodyMatches(
        "y = Sqr(x)",
        "y = Math.Sqrt(x);");

    [TestMethod]
    public void Log() => ValidateBodyMatches(
        "y = Log(x)",
        "y = Math.Log(x);");

    [TestMethod]
    public void Exp() => ValidateBodyMatches(
        "y = Exp(x)",
        "y = Math.Exp(x);");

    [TestMethod]
    public void Sgn() => ValidateBodyMatches(
        "y = Sgn(x)",
        "y = Math.Sign(x);");

    [TestMethod]
    public void Int() => ValidateBodyMatches(
        "y = Int(x)",
        "y = (int)Math.Floor((double)x);");

    [TestMethod]
    public void Fix() => ValidateBodyMatches(
        "y = Fix(x)",
        "y = (int)Math.Truncate((double)x);");

    [TestMethod]
    public void Round1() => ValidateBodyMatches(
        "y = Round(x)",
        "y = Math.Round(x);");

    [TestMethod]
    public void Round2() => ValidateBodyMatches(
        "y = Round(x, 2)",
        "y = Math.Round(x, 2);");

    // ── Type conversions ──────────────────────────────────────────────────────

    [TestMethod]
    public void CInt() => ValidateBodyMatches(
        "y = CInt(x)",
        "y = Convert.ToInt32(x);");

    [TestMethod]
    public void CShort() => ValidateBodyMatches(
        "y = CShort(x)",
        "y = Convert.ToInt16(x);");

    [TestMethod]
    public void CSng() => ValidateBodyMatches(
        "y = CSng(x)",
        "y = Convert.ToSingle(x);");

    [TestMethod]
    public void CBool() => ValidateBodyMatches(
        "y = CBool(x)",
        "y = Convert.ToBoolean(x);");

    [TestMethod]
    public void CByte() => ValidateBodyMatches(
        "y = CByte(x)",
        "y = Convert.ToByte(x);");

    [TestMethod]
    public void CDate() => ValidateBodyMatches(
        "y = CDate(x)",
        "y = Convert.ToDateTime(x);");

    [TestMethod]
    public void CCur() => ValidateBodyMatches(
        "y = CCur(x)",
        "y = Convert.ToDecimal(x);");

    // ── Strings ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Trim() => ValidateBodyMatches(
        "y = Trim(s)",
        "y = Strings.Trim((string)s);");

    [TestMethod]
    public void LTrim() => ValidateBodyMatches(
        "y = LTrim(s)",
        "y = Strings.LTrim((string)s);");

    [TestMethod]
    public void RTrim() => ValidateBodyMatches(
        "y = RTrim(s)",
        "y = Strings.RTrim((string)s);");

    [TestMethod]
    public void LCase() => ValidateBodyMatches(
        "y = LCase(s)",
        "y = Strings.LCase((string)s);");

    [TestMethod]
    public void UCase() => ValidateBodyMatches(
        "y = UCase(s)",
        "y = Strings.UCase((string)s);");

    [TestMethod]
    public void Right() => ValidateBodyMatches(
        "y = Right(s, 3)",
        "y = Strings.Right((string)s, 3);");

    [TestMethod]
    public void Mid2() => ValidateBodyMatches(
        "y = Mid(s, 2)",
        "y = Strings.Mid((string)s, 2);");

    [TestMethod]
    public void Mid3() => ValidateBodyMatches(
        "y = Mid(s, 2, 3)",
        "y = Strings.Mid((string)s, 2, 3);");

    [TestMethod]
    public void Space() => ValidateBodyMatches(
        "y = Space(5)",
        "y = Strings.Space(5);");

    [TestMethod]
    public void InStr2() => ValidateBodyMatches(
        """y = InStr(s, "a")""",
        """y = Strings.InStr((string)s, "a");""");

    [TestMethod]
    public void InStr3() => ValidateBodyMatches(
        """y = InStr(2, s, "a")""",
        """y = Strings.InStr(2, (string)s, "a");""");

    [TestMethod]
    public void Left() => ValidateBodyMatches(
        "y = Left(s, 3)",
        "y = Strings.Left((string)s, 3);");

    [TestMethod]
    public void Len() => ValidateBodyMatches(
        "y = Len(s)",
        "y = Strings.Len((string)s);");

    [TestMethod]
    public void Replace() => ValidateBodyMatches(
        """y = Replace(s, "a", "b")""",
        """y = Strings.Replace((string)s, "a", "b");""");

    // ── Date/Time identifiers ─────────────────────────────────────────────────

    [TestMethod]
    public void TimeIdentifier() => ValidateBodyMatches(
        "y = Time",
        "y = System.DateTime.Now.TimeOfDay;");

    // ── IsMissing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsMissing_Positive() => ValidateMemberMatches(
        """
        Public Sub Test(Optional x As Long)
            If IsMissing(x) Then DoSomething
        End Sub
        """,
        """
        public static void Test(int x = default)
        {
            if (x == default)
                DoSomething();
        }
        """);

    [TestMethod]
    public void IsMissing_Negated() => ValidateMemberMatches(
        """
        Public Sub Test(Optional x As Long)
            If Not IsMissing(x) Then DoSomething
        End Sub
        """,
        """
        public static void Test(int x = default)
        {
            if (x != default)
                DoSomething();
        }
        """);

    [TestMethod]
    public void IsMissing_Object_Negated() => ValidateMemberMatches(
        """
        Public Sub Test(Optional x As Variant)
            If Not IsMissing(x) Then DoSomething
        End Sub
        """,
        """
        public static void Test(object x = default)
        {
            if (x != default)
                DoSomething();
        }
        """);
}
