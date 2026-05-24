using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class IOTests
{
    [TestMethod]
    public void OpenReadModeUsesFileOpenWithInputMode()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileOpen");
        body.Should().Contain("OpenMode.Input");
    }

    [TestMethod]
    public void OpenOutputModeUsesFileOpenWithOutputMode()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Output As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileOpen");
        body.Should().Contain("OpenMode.Output");
    }

    [TestMethod]
    public void OpenAppendModeUsesFileOpenWithAppendMode()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Append As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileOpen");
        body.Should().Contain("OpenMode.Append");
    }

    [TestMethod]
    public void OpenInputSharedUsesFileOpenWithSharedShare()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input Shared As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("OpenMode.Input");
        body.Should().Contain("OpenShare.Shared");
    }

    [TestMethod]
    public void OpenWithAccessReadUsesOpenAccessRead()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input Access Read As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("OpenAccess.Read");
    }

    [TestMethod]
    public void PrintStatementUsesPrintLine()
    {
        var conversion = ConvertBody(
            """
            Print #1, x
            """);

        GetBodyText(conversion).Should().Contain("PrintLine");
    }

    [TestMethod]
    public void PrintWithTrailingSemicolonUsesPrint()
    {
        var conversion = ConvertBody(
            """
            Print #1, x;
            """);

        GetBodyText(conversion).Should().Contain("Print(");
        GetBodyText(conversion).Should().NotContain("PrintLine");
    }

    [TestMethod]
    public void PrintSpcDoesNotProduceError()
    {
        var conversion = ConvertBody(
            """
            Print #1, SPC(3)
            """);

        conversion.TransformErrors.Should().BeEmpty();
        GetBodyText(conversion).Should().Contain("SPC(");
    }

    [TestMethod]
    public void PrintTabDoesNotProduceError()
    {
        var conversion = ConvertBody(
            """
            Print #1, TAB(5)
            """);

        conversion.TransformErrors.Should().BeEmpty();
        GetBodyText(conversion).Should().Contain("TAB(");
    }

    [TestMethod]
    public void CloseStatementUsesFileClose()
    {
        var conversion = ConvertBody(
            """
            Close #1
            """);

        GetBodyText(conversion).Should().Contain("FileClose");
    }

    [TestMethod]
    public void CloseWithNoArgsUsesFileCloseWithNoArgs()
    {
        var conversion = ConvertBody(
            """
            Close
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileClose()");
    }

    [TestMethod]
    public void CloseWithMultipleFileNumbersPassesAll()
    {
        var conversion = ConvertBody(
            """
            Dim f1 As Integer
            Dim f2 As Integer
            Close f1, f2
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileClose");
        body.Should().Contain("f1");
        body.Should().Contain("f2");
    }

    [TestMethod]
    public void LineInputStatementAssignsReturnValue()
    {
        var conversion = ConvertBody(
            """
            Line Input #1, lineText
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("LineInput(");
        body.Should().Contain("lineText");
        body.Should().Contain("=");
    }

    [TestMethod]
    public void WriteStatementPassesFileNumAndItems()
    {
        var conversion = ConvertBody(
            """
            Write #1, x, y
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Write(");
        body.Should().Contain("1");
    }

    [TestMethod]
    public void InputStatementUsesRefArguments()
    {
        var conversion = ConvertBody(
            """
            Input #1, x, y
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Input(");
        body.Should().Contain("ref");
    }

    [TestMethod]
    public void InputStatementProducesOneCallPerVariable()
    {
        var conversion = ConvertBody(
            """
            Input #1, x, y
            """);

        var method = conversion.Class.Members.OfType<MethodDeclarationSyntax>().Single();
        method.Body!.Statements.Should().HaveCount(2);
    }

    [TestMethod]
    public void PutStatementUsesFilePut()
    {
        var conversion = ConvertBody(
            """
            Put #1, , x
            """);

        GetBodyText(conversion).Should().Contain("FilePut(");
    }

    [TestMethod]
    public void GetStatementUsesFileGet()
    {
        var conversion = ConvertBody(
            """
            Get #1, , x
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileGet(");
        body.Should().Contain("ref");
    }

    [TestMethod]
    public void SeekStatementUsesSeek()
    {
        var conversion = ConvertBody(
            """
            Seek #1, 100
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Seek(");
        body.Should().Contain("100");
    }

    [TestMethod]
    public void KillStatementUsesKill()
    {
        var conversion = ConvertBody(
            """
            Kill filePath
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Kill(");
    }


    static VB6ToCSharpConversion ConvertBody(string vb, string? name = null)
    {
        var wrapper = $"""
        Sub Test()
            {vb}
        End Sub
        """;

        var conversion = VB6ToCSharpConversion.ConvertString(wrapper, name ?? nameof(IOTests));
        conversion.ParseErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();
        return conversion;
    }

    static string GetBodyText(VB6ToCSharpConversion conversion)
    {
        var method = conversion.Class.Members.OfType<MethodDeclarationSyntax>().Single();
        var sb = new StringBuilder();

        foreach (var statement in method.Body!.Statements) {
            sb.AppendLine(statement.NormalizeWhitespace().ToFullString());
        }

        return sb.ToString();
    }
}
