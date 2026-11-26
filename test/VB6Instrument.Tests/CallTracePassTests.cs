#nullable enable
using AwesomeAssertions;

namespace VB6Instrument.Tests;

[TestClass]
public class CallTracePassTests
{
    [TestMethod]
    public void PrependsTemplatedTraceAtBaselineIndent()
    {
        var output = TestSources.RunTraces(
            """
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            "Debug.Write \"{File}.{Member}\"",
            fileName: "modMain.bas");

        output.Should().Be(TestSources.Crlf(
            """
            Public Sub CloseSale()
                      Debug.Write "modMain.CloseSale"
                x = 1
            End Sub
            """));
    }

    [TestMethod]
    public void PlaceholderMatchingIsCaseInsensitive()
    {
        var output = TestSources.RunTraces(
            """
            Attribute VB_Name = "Form1"
            Public Sub CloseSale()
                x = 1
            End Sub
            """,
            "ErrorHandle \"{file}\", \"{member}()\"",
            fileName: "Form1.frm");

        output.Should().Contain("ErrorHandle \"Form1\", \"CloseSale()\"");
    }

    [TestMethod]
    public void PropertiesAreTraced()
    {
        var output = TestSources.RunTraces(
            """
            Public Property Get Value() As Long
                Value = 1
            End Property

            Public Property Let Value(ByVal rhs As Long)
                m_value = rhs
            End Property
            """,
            "Debug.Write \"{File}.{Member}\"",
            fileName: "test.bas");

        output.Should().Contain("Debug.Write \"test.Value\"");
    }

    [TestMethod]
    public void NoTraceMarkerSkipsMember()
    {
        var output = TestSources.RunTraces(
            """
            Public Sub Noisy()
                x = 1
            End Sub

            Public Sub Quiet() ' [NoTrace]
                x = 1
            End Sub
            """,
            "Debug.Write \"{File}.{Member}\"");

        output.Should().Contain("Debug.Write");
        var quietIndex = output.IndexOf("Public Sub Quiet()", StringComparison.Ordinal);
        var endIndex = output.IndexOf("End Sub", quietIndex, StringComparison.Ordinal);
        output[quietIndex..endIndex].Should().NotContain("Debug.Write");
    }

    [TestMethod]
    public void ReRunDetectsExistingTraceAndDoesNotDuplicate()
    {
        const string source = """
            Public Sub CloseSale()
                x = 1
            End Sub
            """;
        var template = "Debug.Write \"{File}.{Member}\"";
        var once = TestSources.RunTraces(source, template);
        var twice = TestSources.RunTraces(once, template);
        twice.Should().Be(once);
    }

    [TestMethod]
    public void ReRunInsideNumberedMemberDetectsExistingTrace()
    {
        const string source = """
            Public Sub CloseSale()
            10        x = 1
            End Sub
            """;
        var template = "Debug.Write \"{File}.{Member}\"";
        var once = TestSources.RunTraces(source, template);
        once.Should().Contain("          Debug.Write");
        var twice = TestSources.RunTraces(once, template);
        twice.Should().Be(once);
    }

    [TestMethod]
    public void EmptyAndCommentOnlyBodiesAreSkipped()
    {
        var output = TestSources.RunTraces(
            """
            Public Sub Empty()
            End Sub

            Public Sub CommentOnly()
                ' nothing
            End Sub
            """,
            "Debug.Write \"{File}.{Member}\"");

        output.Should().NotContain("Debug.Write");
    }

    [TestMethod]
    public void TraceRunsAfterLineNumbersKeepsErlPositionsValid()
    {
        var output = TestSources.RunAll(
            """
            Public Sub CloseSale()
                x = 1
                y = 2
            End Sub
            """,
            "Debug.Write \"{File}.{Member}\"");

        // The trace is unnumbered at the baseline; the first original
        // statement keeps Erl 10.
        var traceIndex = output.IndexOf("Debug.Write", StringComparison.Ordinal);
        var firstStatement = output.IndexOf("x = 1", traceIndex, StringComparison.Ordinal);
        var lineStart = output.LastIndexOf('\n', firstStatement) + 1;
        output[lineStart..firstStatement].TrimStart().Should().StartWith("10");
    }
}
