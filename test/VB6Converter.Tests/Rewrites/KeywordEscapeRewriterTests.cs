using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class KeywordEscapeRewriterTests
{
    // ── Local variables ───────────────────────────────────────────────────────

    [TestMethod]
    public void LocalVariable_KeywordName_Escaped() => ValidateBodyMatches(
        "Dim default As String",
        "string @default = default;");

    [TestMethod]
    public void LocalVariable_KeywordName_UsageEscaped() => ValidateBodyMatches(
        """
        Dim default As String
        default = "foo"
        """,
        """
        string @default = default;
        @default = "foo";
        """);

    [TestMethod]
    public void LocalVariable_NonKeyword_Unchanged() => ValidateBodyMatches(
        "Dim myVar As String",
        "string myVar = default;");

    // ── Parameters ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Parameter_KeywordName_Escaped() => ValidateMemberMatches(
        """
        Public Sub Test(ByVal default As String)
        End Sub
        """,
        """
        public static void Test(string @default)
        {
        }
        """);

    [TestMethod]
    public void Parameter_KeywordName_BodyUsageEscaped() => ValidateMemberMatches(
        """
        Public Sub Test(ByVal default As String)
            Dim x As String
            x = default
        End Sub
        """,
        """
        public static void Test(string @default)
        {
            string x = default;
            x = @default;
        }
        """);

    [TestMethod]
    public void Parameter_NonKeyword_Unchanged() => ValidateMemberMatches(
        """
        Public Sub Test(ByVal value As String)
        End Sub
        """,
        """
        public static void Test(string value)
        {
        }
        """);

    // ── Multiple keywords ─────────────────────────────────────────────────────

    [TestMethod]
    public void MultipleKeywordParams_AllEscaped() => ValidateMemberMatches(
        """
        Public Sub Test(ByVal default As String, ByVal namespace As Long)
        End Sub
        """,
        """
        public static void Test(string @default, int @namespace)
        {
        }
        """);

    [TestMethod]
    public void ThisReceiver_IsNotEscaped() => ValidateBodyMatches(
        """
        With Me.ActiveControl
            If (.Name = "mskPrescricaoO") Then
                oUtils.ModificarValor mskPrescricaoO(.Index)
            End If
        End With
        """,
        """
        if ((this.ActiveControl.Name == "mskPrescricaoO"))
        {
            oUtils.ModificarValor(mskPrescricaoO(this.ActiveControl.Index));
        }
        """);
}
