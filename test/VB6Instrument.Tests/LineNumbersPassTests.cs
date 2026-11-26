#nullable enable
using AwesomeAssertions;

namespace VB6Instrument.Tests;

[TestClass]
public class LineNumbersPassTests
{
    [TestMethod]
    public void NumbersExecutableStatementsAtBaseline()
    {
        var output = TestSources.RunLineNumbers(
            """
            Attribute VB_Name = "modT"
            Option Explicit

            Public Sub Foo()
                Dim x As Long
                x = 1
                If x > 0 Then
                    x = 2
                End If
            End Sub
            """);

        output.Should().Be(TestSources.Crlf(
            """
            Attribute VB_Name = "modT"
            Option Explicit

            Public Sub Foo()
                      Dim x As Long
            10        x = 1
            20        If x > 0 Then
            30            x = 2
            40        End If
            End Sub
            """));
    }

    [TestMethod]
    public void FirstCaseAfterSelectCaseIsNeverNumbered()
    {
        var output = TestSources.RunLineNumbers(
            """
            Public Sub Pick(ByVal code As Long)
                Select Case code
                    Case 1
                        code = 1
                    Case 2
                        code = 2
                End Select
            End Sub
            """);

        var lines = output.Split("\r\n");
        var selectIndex = Array.FindIndex(lines, l => l.Contains("Select Case"));
        lines[selectIndex].Should().StartWith("10       ");
        lines[selectIndex + 1].Should().NotMatchRegex(@"^\d+").And.Contain("Case 1");
        lines[selectIndex + 2].Should().MatchRegex(@"^\d+").And.Contain("code = 1");
        lines[selectIndex + 3].Should().MatchRegex(@"^\d+").And.Contain("Case 2");
    }

    [TestMethod]
    public void ColumnOneLabelKeepsLegacyOneSpaceForm()
    {
        var output = TestSources.RunLineNumbers(
            """
            Public Sub Retry()
                Dim attempts As Long
                attempts = 0
            RetryPoint:
                attempts = attempts + 1
                If attempts < 3 Then GoTo RetryPoint
                Exit Sub
            Helper:
                attempts = attempts * 2
                Return
            End Sub

            Public Sub Again()
            StartOver: x = 1
                GoTo StartOver
            End Sub
            """);

        var lines = output.Split("\r\n");
        // Bare labels keep their column-1 position and take no number.
        lines.Should().Contain(l => l.StartsWith("RetryPoint:") && !l.StartsWith(" "));
        lines.Should().Contain(l => l.StartsWith("Helper:") && !l.StartsWith(" "));
        // Label+statement lines keep the legacy "N Label:" form.
        lines.Should().Contain(l => Regex1().IsMatch(l));
    }

    [TestMethod]
    public void AlreadyNumberedMemberIsSkipped()
    {
        var source = """
            Public Sub Foo()
            10        x = 1
            20        x = 2
            End Sub
            """;
        TestSources.RunLineNumbers(source).Should().Be(TestSources.Crlf(source));
    }

    [TestMethod]
    public void BareDimAndCommentsAreNeverNumbered()
    {
        var output = TestSources.RunLineNumbers(
            """
            Public Sub Foo()
                Dim x As Long
                ' a comment
                x = 1
            End Sub
            """);

        var lines = output.Split("\r\n");
        lines.Should().Contain(l => l.Contains("Dim x As Long") && !l.StartsWith("10"));
        lines.Should().Contain(l => l.Contains("' a comment") && !l.StartsWith("10"));
        lines.Should().Contain(l => l.StartsWith("10") && l.Contains("x = 1"));
    }

    [TestMethod]
    public void MemberOverflowThrows()
    {
        var body = string.Join("\r\n", Enumerable.Range(0, 6600).Select(_ => "    x = 1"));
        var source = $"Public Sub Foo()\r\n{body}\r\nEnd Sub\r\n";
        var act = () => TestSources.RunLineNumbers(source);
        act.Should().Throw<InstrumentationException>().WithMessage("*needs a line number above 65535*");
    }

    private static System.Text.RegularExpressions.Regex Regex1() =>
        new(@"^\d+ [A-Za-z_]\w*:", System.Text.RegularExpressions.RegexOptions.Compiled);
}
