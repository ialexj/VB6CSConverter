#nullable enable
using System.Text;
using AwesomeAssertions;

namespace VB6Instrument.Tests;

/// <summary>Full-pipeline idempotence: instrumenting an already instrumented
/// file must leave it unchanged.</summary>
[TestClass]
public class IdempotenceTests
{
    private const string TraceTemplate = "Debug.Write \"{File}.{Member}\"";

    [TestMethod]
    public void AllPassesSecondRunChangesNothing()
    {
        const string source = """
            Attribute VB_Name = "modFull"
            Option Explicit

            Public Sub CloseSale()
                Dim x As Long
                x = 1
                If x > 0 Then
                    x = 2
                End If
            End Sub

            Private Sub Text1_Change()
                Text1.Text = "changed"
            End Sub

            Private Sub Class_Terminate()
                Cleanup
            End Sub

            Private Function Compute(ByVal n As Long) As Long
                Compute = n * 2
            End Function
            """;

        var once = TestSources.RunAll(source, TraceTemplate, fileName: "modFull.bas");
        var twice = TestSources.RunAll(once, TraceTemplate, fileName: "modFull.bas");
        twice.Should().Be(once);
    }

    [TestMethod]
    public void PartialSecondRunWithAllPassesKeepsExistingInstrumentation()
    {
        const string source = """
            Public Sub CloseSale()
                x = 1
            End Sub

            Private Sub Text1_Change()
                x = 1
            End Sub
            """;

        var numberedOnly = TestSources.RunLineNumbers(source);
        var allPasses = TestSources.RunAll(numberedOnly, TraceTemplate, fileName: "modPartial.bas");

        // Re-running the full set over the result must now be a no-op.
        var again = TestSources.RunAll(allPasses, TraceTemplate, fileName: "modPartial.bas");
        again.Should().Be(allPasses);
    }

    [TestMethod]
    public void GuardsOnlySecondRunChangesNothing()
    {
        const string source = """
            Public Sub CloseSale()
                x = 1
            End Sub
            """;
        var once = TestSources.RunGuards(source);
        var twice = TestSources.RunGuards(once);
        twice.Should().Be(once);
    }

    [TestMethod]
    public void SwallowStatementSurvivesAsErrClearOnlyWhenTemplateOmitted()
    {
        const string source = """
            Private Sub Text1_Change()
                x = 1
            End Sub
            """;
        var once = TestSources.RunGuards(source, "ErrorHandle \"{file}\", \"{member}()\"");
        var twice = TestSources.RunGuards(once);
        twice.Should().Be(once);
    }
}
