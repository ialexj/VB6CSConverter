#nullable enable
using AwesomeAssertions;

namespace VB6Instrument.Tests;

[TestClass]
public class ErrorGuardPassTests
{
    [TestMethod]
    public void PublicMemberGetsReraiseHandler()
    {
        var output = TestSources.RunGuards(
            """
            Attribute VB_Name = "Form1"
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            fileName: "Form1.frm");

        // Default strategy is Raise (probe result: fall-to-End does not
        // propagate), so the handler decorates with the captured Erl and
        // re-raises explicitly.
        output.Should().Be(TestSources.Crlf(
            """
            Attribute VB_Name = "Form1"
            Public Sub CloseSale()
                      On Error GoTo GuardError_CloseSale
                x = 1
                      Exit Sub
            GuardError_CloseSale:
                      Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl
                      Err.Raise Err.Number, Err.Source, "Form1.CloseSale (" & instrumentedGuardErl & ") -> " & Err.Description
            End Sub
            """));
    }

    [TestMethod]
    public void FunctionMemberGetsExitFunction()
    {
        var output = TestSources.RunGuards(
            """
            Attribute VB_Name = "Form1"
            Public Function GetValue() As Long
                x = 1
            End Function
            """,
            fileName: "Form1.frm");

        // Exit Sub inside a Function is a VB6 compile error — the exit
        // statement must follow the member kind.
        output.Should().Be(TestSources.Crlf(
            """
            Attribute VB_Name = "Form1"
            Public Function GetValue() As Long
                      On Error GoTo GuardError_GetValue
                x = 1
                      Exit Function
            GuardError_GetValue:
                      Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl
                      Err.Raise Err.Number, Err.Source, "Form1.GetValue (" & instrumentedGuardErl & ") -> " & Err.Description
            End Function
            """));
    }

    [TestMethod]
    public void PropertyMembersGetExitProperty()
    {
        var output = TestSources.RunGuards(
            """
            Attribute VB_Name = "Module1"
            Public Property Get Thing() As Long
                x = 1
            End Property

            Public Property Let Thing(ByVal Value As Long)
                x = 2
            End Property

            Public Property Set Thing(ByVal Value As Object)
                x = 3
            End Property
            """,
            fileName: "Module1.bas");

        // Get, Let and Set all exit with Exit Property; the uniqueness of
        // the generated label is scoped to each member's range.
        output.Should().Be(TestSources.Crlf(
            """
            Attribute VB_Name = "Module1"
            Public Property Get Thing() As Long
                      On Error GoTo GuardError_Thing
                x = 1
                      Exit Property
            GuardError_Thing:
                      Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl
                      Err.Raise Err.Number, Err.Source, "Module1.Thing (" & instrumentedGuardErl & ") -> " & Err.Description
            End Property

            Public Property Let Thing(ByVal Value As Long)
                      On Error GoTo GuardError_Thing
                x = 2
                      Exit Property
            GuardError_Thing:
                      Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl
                      Err.Raise Err.Number, Err.Source, "Module1.Thing (" & instrumentedGuardErl & ") -> " & Err.Description
            End Property

            Public Property Set Thing(ByVal Value As Object)
                      On Error GoTo GuardError_Thing
                x = 3
                      Exit Property
            GuardError_Thing:
                      Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl
                      Err.Raise Err.Number, Err.Source, "Module1.Thing (" & instrumentedGuardErl & ") -> " & Err.Description
            End Property
            """));
    }

    [TestMethod]
    public void FallThroughStrategyDecoratesWithoutReraise()
    {
        var output = TestSources.RunGuards(
            """
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            propagation: ErrorPropagation.FallThrough,
            fileName: "test.bas");

        output.Should().Contain("Err.Description = \"test.CloseSale (\" & Erl & \") -> \" & Err.Description");
        output.Should().NotContain("Err.Raise");
    }

    [TestMethod]
    public void PrivateEventHandlerGetsSwallowHandler()
    {
        var output = TestSources.RunGuards(
            """
            Private Sub Text1_Change()
                x = 1
            End Sub
            """,
            "ErrorHandle \"{file}\", \"{member}()\"",
            fileName: "test.bas");

        output.Should().Be(TestSources.Crlf(
            """
            Private Sub Text1_Change()
                      On Error GoTo GuardError_Text1_Change
                x = 1
                      Exit Sub
            GuardError_Text1_Change:
                      ErrorHandle "test", "Text1_Change()"
                      Err.Clear
            End Sub
            """));
    }

    [TestMethod]
    public void SwallowWithoutStatementEmitsErrClearOnly()
    {
        var output = TestSources.RunGuards(
            """
            Private Sub Text1_Change()
                x = 1
            End Sub
            """);

        output.Should().Contain("GuardError_Text1_Change:");
        var labelIndex = output.IndexOf("GuardError_Text1_Change:", StringComparison.Ordinal);
        var endSubIndex = output.IndexOf("End Sub", labelIndex, StringComparison.Ordinal);
        output[labelIndex..endSubIndex].Should().Contain("Err.Clear").And.NotContain("ErrorHandle");
    }

    [TestMethod]
    public void GuardRunsAfterTraceSoOnErrorPrecedesTrace()
    {
        var output = TestSources.RunAll(
            """
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            "Debug.Write \"{File}.{Member}\"");

        var onErrorIndex = output.IndexOf("On Error GoTo", StringComparison.Ordinal);
        var traceIndex = output.IndexOf("Debug.Write", StringComparison.Ordinal);
        onErrorIndex.Should().BePositive().And.BeLessThan(traceIndex);
    }

    [TestMethod]
    public void ClassificationMatrix()
    {
        AssertMode("Public Sub Foo()", GuardMode.Reraise);
        AssertMode("Friend Sub Foo()", GuardMode.Reraise);
        AssertMode("Global Sub Foo()", GuardMode.Reraise);
        AssertMode("Sub Foo()", GuardMode.Reraise);
        AssertMode("Private Sub Foo()", GuardMode.Reraise);
        AssertMode("Private Sub Text1_Change()", GuardMode.Swallow);
        AssertMode("Private Sub cmd_OK_Click()", GuardMode.Swallow);
        AssertMode("Private Sub Form_Load()", GuardMode.Swallow);
        AssertMode("Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)", GuardMode.Swallow);
        AssertMode("Private Sub Class_Terminate()", GuardMode.Swallow);
        AssertMode("Private Sub Class_Initialize()", GuardMode.Reraise);
        AssertMode("Private Sub UserControl_Initialize()", GuardMode.Swallow);
        AssertMode("Private Sub UserControl_Resize()", GuardMode.Swallow);
        AssertMode("Private Sub UserControl_Terminate()", GuardMode.Swallow);
        AssertMode("Private Sub Winsock1_DataArrival(ByVal bytesTotal As Long)", GuardMode.Swallow);
    }

    private static void AssertMode(string signature, GuardMode expected)
    {
        var range = Vb6MemberScanner.ScanMembers(
            "test.bas",
            Vb6Lexer.Tokenize([signature, "    x = 1", "End Sub"], 1)).Single();
        ErrorGuardPass.Classify(range).Should().Be(expected, because: signature);
    }

    [TestMethod]
    public void ExistingOnErrorSkipsMember()
    {
        var output = TestSources.RunGuards(
            """
            Private Sub Managed()
                On Error GoTo Bail
                x = 1
                Exit Sub
            Bail:
                x = 0
            End Sub
            """);

        output.Should().NotContain("GuardError_");
    }

    [TestMethod]
    public void OnErrorInsideStringLiteralDoesNotSkipMember()
    {
        var output = TestSources.RunGuards(
            """
            Private Sub Text1_Change()
                s = "On Error GoTo 0"
                x = 1
            End Sub
            """);

        output.Should().Contain("On Error GoTo GuardError_Text1_Change");
    }

    [TestMethod]
    public void OnErrorInsideCommentDoesNotSkipMember()
    {
        var output = TestSources.RunGuards(
            """
            Private Sub Text1_Change()
                ' the old code used On Error Resume Next here
                x = 1
            End Sub
            """);

        output.Should().Contain("On Error GoTo GuardError_Text1_Change");
    }

    [TestMethod]
    public void OnErrorInColonCompoundSkipsMember()
    {
        var output = TestSources.RunGuards(
            """
            Private Sub Text1_Change()
                x = 1: On Error GoTo 0
            End Sub
            """);

        output.Should().NotContain("GuardError_");
    }

    [TestMethod]
    public void LabelCollisionSuffixUniquifies()
    {
        var output = TestSources.RunGuards(
            """
            Public Sub CloseSale()
                x = 1
                GoTo GuardError_CloseSale
            GuardError_CloseSale:
                x = 2
            End Sub
            """);

        output.Should().Contain("On Error GoTo GuardError_CloseSale_2");
        output.Should().Contain("GuardError_CloseSale_2:");
    }

    [TestMethod]
    public void NoGuardMarkerSkipsMember()
    {
        var output = TestSources.RunGuards(
            """
            Public Sub CloseSale() ' [NoGuard]
                x = 1
            End Sub
            """);

        output.Should().NotContain("GuardError_");
    }

    [TestMethod]
    public void BracketedMemberNameIsSanitizedForLabel()
    {
        var output = TestSources.RunGuards(
            """
            Public Sub [Get Data]()
                x = 1
            End Sub
            """);

        output.Should().Contain("On Error GoTo GuardError_Get_Data");
        output.Should().Contain("GuardError_Get_Data:");
    }

    [TestMethod]
    public void RaisePropagationBakesCapturedErl()
    {
        var output = TestSources.RunGuards(
            """
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            propagation: ErrorPropagation.Raise,
            fileName: "test.bas");

        output.Should().Contain("Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl");
        output.Should().Contain("Err.Raise Err.Number, Err.Source, \"test.CloseSale (\" & instrumentedGuardErl & \") -> \" & Err.Description");
    }

    [TestMethod]
    public void EmptyAndCommentOnlyBodiesAreSkipped()
    {
        var output = TestSources.RunGuards(
            """
            Public Sub Empty()
            End Sub

            Public Sub CommentOnly()
                ' nothing
            End Sub
            """);

        output.Should().NotContain("GuardError_");
    }

    [TestMethod]
    public void GuardLinesAreUnnumberedSoErlStaysValid()
    {
        var output = TestSources.RunAll(
            """
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            "Debug.Write \"{File}.{Member}\"");

        var xIndex = output.IndexOf("x = 1", StringComparison.Ordinal);
        var lineStart = output.LastIndexOf('\n', xIndex) + 1;
        output[lineStart..xIndex].TrimStart().Should().StartWith("10");
    }
}
