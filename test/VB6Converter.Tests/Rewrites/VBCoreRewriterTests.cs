using VB6Converter.Rewriters;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class VBCoreRewriterTests
{
    // ── Math ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Abs() => ValidateBodyMatches(
        "y = Abs(x)",
        "y = System.Math.Abs(x);", new VBCoreRewriter());

    [TestMethod]
    public void Sin() => ValidateBodyMatches(
        "y = Sin(x)",
        "y = System.Math.Sin(x);", new VBCoreRewriter());

    [TestMethod]
    public void Cos() => ValidateBodyMatches(
        "y = Cos(x)",
        "y = System.Math.Cos(x);", new VBCoreRewriter());

    [TestMethod]
    public void Tan() => ValidateBodyMatches(
        "y = Tan(x)",
        "y = System.Math.Tan(x);", new VBCoreRewriter());

    [TestMethod]
    public void Atn() => ValidateBodyMatches(
        "y = Atn(x)",
        "y = System.Math.Atan(x);", new VBCoreRewriter());

    [TestMethod]
    public void Sqr() => ValidateBodyMatches(
        "y = Sqr(x)",
        "y = System.Math.Sqrt(x);", new VBCoreRewriter());

    [TestMethod]
    public void Log() => ValidateBodyMatches(
        "y = Log(x)",
        "y = System.Math.Log(x);", new VBCoreRewriter());

    [TestMethod]
    public void Exp() => ValidateBodyMatches(
        "y = Exp(x)",
        "y = System.Math.Exp(x);", new VBCoreRewriter());

    [TestMethod]
    public void Sgn() => ValidateBodyMatches(
        "y = Sgn(x)",
        "y = System.Math.Sign(x);", new VBCoreRewriter());

    [TestMethod]
    public void Int() => ValidateBodyMatches(
        "y = Int(x)",
        "y = (int)System.Math.Floor((double)x);", new VBCoreRewriter());

    [TestMethod]
    public void Fix() => ValidateBodyMatches(
        "y = Fix(x)",
        "y = (int)System.Math.Truncate((double)x);", new VBCoreRewriter());

    [TestMethod]
    public void Round1() => ValidateBodyMatches(
        "y = Round(x)",
        "y = System.Math.Round(x);", new VBCoreRewriter());

    [TestMethod]
    public void Round2() => ValidateBodyMatches(
        "y = Round(x, 2)",
        "y = System.Math.Round(x, 2);", new VBCoreRewriter());

    // ── Type conversions ──────────────────────────────────────────────────────

    [TestMethod]
    public void CInt() => ValidateBodyMatches(
        "y = CInt(x)",
        "y = System.Convert.ToInt32(x);", new VBCoreRewriter());

    [TestMethod]
    public void CShort() => ValidateBodyMatches(
        "y = CShort(x)",
        "y = System.Convert.ToInt16(x);", new VBCoreRewriter());

    [TestMethod]
    public void CSng() => ValidateBodyMatches(
        "y = CSng(x)",
        "y = System.Convert.ToSingle(x);", new VBCoreRewriter());

    [TestMethod]
    public void CBool() => ValidateBodyMatches(
        "y = CBool(x)",
        "y = System.Convert.ToBoolean(x);", new VBCoreRewriter());

    [TestMethod]
    public void CByte() => ValidateBodyMatches(
        "y = CByte(x)",
        "y = System.Convert.ToByte(x);", new VBCoreRewriter());

    [TestMethod]
    public void CDate() => ValidateBodyMatches(
        "y = CDate(x)",
        "y = System.Convert.ToDateTime(x);", new VBCoreRewriter());

    [TestMethod]
    public void CCur() => ValidateBodyMatches(
        "y = CCur(x)",
        "y = System.Convert.ToDecimal(x);", new VBCoreRewriter());

    [TestMethod]
    public void Format() => ValidateBodyMatches(
        "y = Format(x, \"Standard\")",
        "y = Microsoft.VisualBasic.Strings.Format(x, \"Standard\");", new VBCoreRewriter());

    [TestMethod]
    public void Val() => ValidateBodyMatches(
        "y = Val(x)",
        "y = Microsoft.VisualBasic.Conversion.Val(x);", new VBCoreRewriter());

    [TestMethod]
    public void Val_StringArgument() => ValidateBodyMatches(
        "y = Val(s)",
        "y = Microsoft.VisualBasic.Conversion.Val(s);", new VBCoreRewriter());

    // ── Strings ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Trim() => ValidateBodyMatches(
        "y = Trim(s)",
        "y = Microsoft.VisualBasic.Strings.Trim((string)s);", new VBCoreRewriter());

    [TestMethod]
    public void LTrim() => ValidateBodyMatches(
        "y = LTrim(s)",
        "y = Microsoft.VisualBasic.Strings.LTrim((string)s);", new VBCoreRewriter());

    [TestMethod]
    public void RTrim() => ValidateBodyMatches(
        "y = RTrim(s)",
        "y = Microsoft.VisualBasic.Strings.RTrim((string)s);", new VBCoreRewriter());

    [TestMethod]
    public void LCase() => ValidateBodyMatches(
        "y = LCase(s)",
        "y = Microsoft.VisualBasic.Strings.LCase((string)s);", new VBCoreRewriter());

    [TestMethod]
    public void UCase() => ValidateBodyMatches(
        "y = UCase(s)",
        "y = Microsoft.VisualBasic.Strings.UCase((string)s);", new VBCoreRewriter());

    [TestMethod]
    public void Right() => ValidateBodyMatches(
        "y = Right(s, 3)",
        "y = Microsoft.VisualBasic.Strings.Right((string)s, 3);", new VBCoreRewriter());

    [TestMethod]
    public void Mid2() => ValidateBodyMatches(
        "y = Mid(s, 2)",
        "y = Microsoft.VisualBasic.Strings.Mid((string)s, 2);", new VBCoreRewriter());

    [TestMethod]
    public void Mid3() => ValidateBodyMatches(
        "y = Mid(s, 2, 3)",
        "y = Microsoft.VisualBasic.Strings.Mid((string)s, 2, 3);", new VBCoreRewriter());

    [TestMethod]
    public void Space() => ValidateBodyMatches(
        "y = Space(5)",
        "y = Microsoft.VisualBasic.Strings.Space(5);", new VBCoreRewriter());

    [TestMethod]
    public void StringRepeat_NumericCode() => ValidateBodyMatches(
        "y = String$(255, 0)",
        "y = new string ((char)0, 255);", new VBCoreRewriter());

    [TestMethod]
    public void StringRepeat_CharCode() => ValidateBodyMatches(
        "y = String$(10, 65)",
        "y = new string ((char)65, 10);", new VBCoreRewriter());

    [TestMethod]
    public void InStr2() => ValidateBodyMatches(
        """y = InStr(s, "a")""",
        """y = Microsoft.VisualBasic.Strings.InStr((string)s, "a");""", new VBCoreRewriter());

    [TestMethod]
    public void InStr3() => ValidateBodyMatches(
        """y = InStr(2, s, "a")""",
        """y = Microsoft.VisualBasic.Strings.InStr(2, (string)s, "a");""", new VBCoreRewriter());

    [TestMethod]
    public void Left() => ValidateBodyMatches(
        "y = Left(s, 3)",
        "y = Microsoft.VisualBasic.Strings.Left((string)s, 3);", new VBCoreRewriter());

    [TestMethod]
    public void Len() => ValidateBodyMatches(
        "y = Len(s)",
        "y = Microsoft.VisualBasic.Strings.Len((string)s);", new VBCoreRewriter());

    [TestMethod]
    public void Replace() => ValidateBodyMatches(
        """y = Replace(s, "a", "b")""",
        """y = Microsoft.VisualBasic.Strings.Replace((string)s, "a", "b");""", new VBCoreRewriter());

    // ── Date/Time identifiers ─────────────────────────────────────────────────

    [TestMethod]
    public void TimeIdentifier() => ValidateBodyMatches(
        "y = Time",
        "y = System.DateTime.Now.TimeOfDay;", new VBCoreRewriter());

    [TestMethod]
    public void DateMemberAccess() => ValidateBodyMatches(
        "y = Date.Year",
        "y = System.DateTime.Now.Date.Year;", new VBCoreRewriter());

    [TestMethod]
    public void DateStrIdentifier() => ValidateBodyMatches(
        "y = DateStr",
        "y = Microsoft.VisualBasic.DateAndTime.DateString;", new VBCoreRewriter());

    [TestMethod]
    public void TimeStrIdentifier() => ValidateBodyMatches(
        "y = TimeStr",
        "y = Microsoft.VisualBasic.DateAndTime.TimeString;", new VBCoreRewriter());

    [TestMethod]
    public void TimerIdentifier() => ValidateBodyMatches(
        "y = Timer",
        "y = Microsoft.VisualBasic.DateAndTime.Timer;", new VBCoreRewriter());

    // A qualified type name whose rightmost segment matches a rewritten runtime identifier
    // (e.g. "Timer") must not be rewritten to a member access — it's a type reference, not an
    // expression. Regression test for a cast crash in VisitQualifiedName.
    [TestMethod]
    public void QualifiedTypeName_MatchingRuntimeIdentifier_IsNotRewritten() => ValidateMemberMatches(
        "Dim x As VB.Timer",
        "public static VB.Timer x;", new VBCoreRewriter());

    [TestMethod]
    public void CollectionType_IsFullyQualified() => ValidateMemberMatches(
        "Private col As New Collection",
        "private static Microsoft.VisualBasic.Collection col = new();", new VBCoreRewriter());

    // ── Parameterless calls without parentheses ──────────────────────────────

    [TestMethod]
    public void FreeFile_NoParens() => ValidateBodyMatches(
        "iFile = FreeFile",
        "iFile = Microsoft.VisualBasic.FileSystem.FreeFile();", new VBCoreRewriter());

    [TestMethod]
    public void Command_NoParens() => ValidateBodyMatches(
        "y = Command",
        "y = Microsoft.VisualBasic.Interaction.Command();", new VBCoreRewriter());

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
            if ((x == default))
                DoSomething();
        }
        """, new VBCoreRewriter());

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
            if (!(x == default))
                DoSomething();
        }
        """, new VBCoreRewriter());

    [TestMethod]
    public void IsMissing_Object_Negated() => ValidateMemberMatches(
        """
        Public Sub Test(Optional x As Variant)
            If Not IsMissing(x) Then DoSomething
        End Sub
        """,
        """
        public static void Test(dynamic x = default)
        {
            if (!(x == default))
                DoSomething();
        }
        """, new VBCoreRewriter());

    // ── Microsoft.VisualBasic.Information ────────────────────────────────────

    [TestMethod]
    public void IsDate() => ValidateBodyMatches(
        "y = IsDate(x)",
        "y = Microsoft.VisualBasic.Information.IsDate(x);", new VBCoreRewriter());

    [TestMethod]
    public void IsNumeric() => ValidateBodyMatches(
        "y = IsNumeric(x)",
        "y = Microsoft.VisualBasic.Information.IsNumeric(x);", new VBCoreRewriter());

    [TestMethod]
    public void IsError() => ValidateBodyMatches(
        "y = IsError(x)",
        "y = Microsoft.VisualBasic.Information.IsError(x);", new VBCoreRewriter());

    [TestMethod]
    public void IsObject() => ValidateBodyMatches(
        "y = IsObject(x)",
        "y = Microsoft.VisualBasic.Information.IsReference(x);", new VBCoreRewriter());

    [TestMethod]
    public void TypeName() => ValidateBodyMatches(
        "y = TypeName(x)",
        "y = Microsoft.VisualBasic.Information.TypeName(x);", new VBCoreRewriter());

    [TestMethod]
    public void VarType() => ValidateBodyMatches(
        "y = VarType(x)",
        "y = Microsoft.VisualBasic.Information.VarType(x);", new VBCoreRewriter());

    [TestMethod]
    public void QBColor() => ValidateBodyMatches(
        "y = QBColor(x)",
        "y = Microsoft.VisualBasic.Information.QBColor(x);", new VBCoreRewriter());

    [TestMethod]
    public void RGB() => ValidateBodyMatches(
        "y = RGB(r, g, b)",
        "y = Microsoft.VisualBasic.Information.RGB(r, g, b);", new VBCoreRewriter());


    [TestMethod]
    public void Replace2() => ValidateMemberMatches(
        """
        Function SqlStr(ByVal s As String)
            SqlStr = "'" & Replace$(s, "'", "''") & "'"
        End Function
        """,
        """
        public static dynamic SqlStr(string s) => "'" + Microsoft.VisualBasic.Strings.Replace((string)s, "'", "''") + "'";
        """, new VBCoreRewriter());

    [TestMethod]
    public void IsNull() => ValidateBodyMatches(
        "x = IsNull(z)",
        "x = (z is null);", new VBCoreRewriter());
}
