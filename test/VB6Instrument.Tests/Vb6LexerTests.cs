#nullable enable
using AwesomeAssertions;

namespace VB6Instrument.Tests;

[TestClass]
public class Vb6LexerTests
{
    [TestMethod]
    public void SplitsColonCompounds()
    {
        var statements = Vb6Lexer.Tokenize(["    a = 1: b = 2"], 1);
        statements.Should().HaveCount(2);
        statements[0].Text.Should().Be("a = 1");
        statements[1].Text.Should().Be("b = 2");
        statements.All(s => s.StartLine == 1).Should().BeTrue();
    }

    [TestMethod]
    public void DoesNotSplitNamedArgumentColon()
    {
        var statements = Vb6Lexer.Tokenize(["    x = Foo(Value:=1)"], 1);
        statements.Should().ContainSingle().Which.Text.Should().Be("x = Foo(Value:=1)");
    }

    [TestMethod]
    public void FoldsContinuations()
    {
        var statements = Vb6Lexer.Tokenize(
        [
            "    x = Foo(1, _",
            "             2, _",
            "             3)",
        ], 1);
        var statement = statements.Should().ContainSingle().Subject;
        statement.Text.Should().StartWith("x = Foo(1,").And.EndWith("3)");
        statement.StartLine.Should().Be(1);
        statement.EndLine.Should().Be(3);
    }

    [TestMethod]
    public void ThrowsOnUnfinishedContinuation()
    {
        var act = () => Vb6Lexer.Tokenize(["    x = Foo(1, _"], 1);
        act.Should().Throw<InstrumentationException>().WithMessage("*Unfinished line continuation*");
    }

    [TestMethod]
    public void SkipsConditionalCompilationDirectives()
    {
        var statements = Vb6Lexer.Tokenize(
        [
            "#If DEBUG Then",
            "    x = 1",
            "#Else",
            "    x = 2",
            "#End If",
        ], 1);
        statements.Select(s => s.Text).Should().BeEquivalentTo(["x = 1", "x = 2"]);
    }

    [TestMethod]
    public void StripsCommentsAndRem()
    {
        var statements = Vb6Lexer.Tokenize(
        [
            "    x = 1 ' trailing comment",
            "    Rem full line",
            "    y = 2",
        ], 1);
        statements.Select(s => s.Text).Should().BeEquivalentTo(["x = 1", "y = 2"]);
    }

    [TestMethod]
    public void CommentApostropheInsideStringIsNotAComment()
    {
        var statements = Vb6Lexer.Tokenize(["    s = \"don't\""], 1);
        statements.Should().ContainSingle().Which.Text.Should().Be("s = \"don't\"");
    }

    [TestMethod]
    public void MaskedTextBlanksStringInteriors()
    {
        var statements = Vb6Lexer.Tokenize(["    s = \"On Error GoTo 0\""], 1);
        var statement = statements.Should().ContainSingle().Subject;
        statement.Text.Should().Contain("On Error");
        statement.MaskedText.Should().NotContain("On Error");
    }

    [TestMethod]
    public void DateLiteralsAreHonoured()
    {
        var statements = Vb6Lexer.Tokenize(["    x = #10:30# : y = 2"], 1);
        // The colon inside the date literal does not split; the standalone colon does.
        statements.Select(s => s.Text).Should().BeEquivalentTo(["x = #10:30#", "y = 2"]);
    }
}
