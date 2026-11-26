#nullable enable
using AwesomeAssertions;

namespace VB6Instrument.Tests;

[TestClass]
public class Vb6MemberScannerTests
{
    private static List<Vb6MemberRange> Scan(params string[] lines) =>
        Vb6MemberScanner.ScanMembers("test.bas", Vb6Lexer.Tokenize(lines, 1));

    [TestMethod]
    public void DetectsSubFunctionAndPropertyRanges()
    {
        var ranges = Scan(
            "Public Sub Foo()",
            "End Sub",
            "",
            "Private Function Bar() As Long",
            "End Function",
            "",
            "Public Property Get Value() As Long",
            "End Property",
            "",
            "Public Property Let Value(ByVal rhs As Long)",
            "End Property");

        ranges.Should().HaveCount(4);
        ranges[0].Name.Should().Be("Foo");
        ranges[0].Kind.Should().Be("Sub");
        ranges[0].StartIndex.Should().Be(0);
        ranges[0].EndIndex.Should().Be(1);
        ranges[1].Name.Should().Be("Bar");
        ranges[2].Kind.Should().Be("Property Get");
        ranges[3].Kind.Should().Be("Property Let");
        ranges[3].Mods.Should().Contain("Public");
    }

    [TestMethod]
    public void MultiLineSignatureFolds()
    {
        var ranges = Scan(
            "Public Sub Wide(ByVal a As Long, _",
            "                 ByVal b As Long)",
            "    x = 1",
            "End Sub");

        var range = ranges.Should().ContainSingle().Subject;
        range.StartIndex.Should().Be(0);
        range.EndIndex.Should().Be(3);
        range.BodyStartIndex.Should().Be(1);
    }

    [TestMethod]
    public void StringLiteralContainingEndSubDoesNotTerminate()
    {
        var ranges = Scan(
            "Public Sub Foo()",
            "    s = \"End Sub\"",
            "End Sub");

        var range = ranges.Should().ContainSingle().Subject;
        range.EndIndex.Should().Be(2);
    }

    [TestMethod]
    public void UnexpectedEndThrows()
    {
        var act = () => Scan("End Sub");
        act.Should().Throw<InstrumentationException>().WithMessage("*Unexpected End Sub at line 1*");
    }

    [TestMethod]
    public void MismatchedEndThrows()
    {
        var act = () => Scan(
            "Public Sub Foo()",
            "End Function");
        act.Should().Throw<InstrumentationException>().WithMessage("*Mismatched End Function for Sub Foo at line 2*");
    }

    [TestMethod]
    public void NestedMemberThrows()
    {
        var act = () => Scan(
            "Public Sub Foo()",
            "    Private Sub Bar()",
            "    End Sub",
            "End Sub");
        act.Should().Throw<InstrumentationException>().WithMessage("*starts before Sub 'Foo' ends*");
    }

    [TestMethod]
    public void UnterminatedMemberThrows()
    {
        var act = () => Scan(
            "Public Sub Foo()",
            "    x = 1");
        act.Should().Throw<InstrumentationException>().WithMessage("*Unterminated Sub Foo at line 1*");
    }

    [TestMethod]
    public void BracketedIdentifiersAreRecognized()
    {
        var ranges = Scan(
            "Public Sub [Get Data]()",
            "    x = 1",
            "End Sub");

        ranges.Should().ContainSingle().Which.Name.Should().Be("[Get Data]");
    }

    [TestMethod]
    public void DesignerFileWithoutVbNameThrows()
    {
        var act = () => Vb6MemberScanner.GetCodeStartIndex(["Option Explicit"], ".frm");
        act.Should().Throw<InstrumentationException>().WithMessage("*must contain Attribute VB_Name*");
    }

    [TestMethod]
    public void DesignerFileWithVbNameFindsCodeStart()
    {
        Vb6MemberScanner.GetCodeStartIndex(
        [
            "VERSION 5.00",
            "Begin Form1",
            "End",
            "Attribute VB_Name = \"Form1\"",
            "Option Explicit",
        ], ".frm").Should().Be(3);
    }
}
