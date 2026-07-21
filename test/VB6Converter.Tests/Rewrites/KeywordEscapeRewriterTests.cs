using VB6Converter.Rewriters;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class KeywordEscapeRewriterTests
{
    // ── Local variables ───────────────────────────────────────────────────────

    [TestMethod]
    public void LocalVariable_KeywordName_Escaped() => ValidateBodyMatches(
        "Dim default As String",
        "string @default = default;", new KeywordEscapeRewriter());

    [TestMethod]
    public void LocalVariable_KeywordName_UsageEscaped() => ValidateBodyMatches(
        """
        Dim default As String
        default = "foo"
        """,
        """
        string @default = default;
        @default = "foo";
        """, new KeywordEscapeRewriter());

    [TestMethod]
    public void LocalVariable_NonKeyword_Unchanged() => ValidateBodyMatches(
        "Dim myVar As String",
        "string myVar = default;", new KeywordEscapeRewriter());

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
        """, new KeywordEscapeRewriter());

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
        """, new KeywordEscapeRewriter());

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
        """, new KeywordEscapeRewriter());

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
        """, new KeywordEscapeRewriter());

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
        """, new KeywordEscapeRewriter());
}
