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
    public void OpenReadModeUsesReadAccessAndNoShare()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input As f
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("File.Open");
        body.Should().Contain("FileAccess.Read");
        body.Should().Contain("FileShare.None");
    }

    [TestMethod]
    public void OpenOutputModeUsesCurrentReadWriteMapping()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Output As f
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileAccess.ReadWrite");
        body.Should().Contain("FileShare.None");
    }

    [TestMethod]
    public void OpenInputSharedUsesReadWriteShare()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input Shared As f
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileAccess.Read");
        body.Should().Contain("FileShare.ReadWrite");
    }

    [TestMethod]
    public void PrintSpcProducesTransformError()
    {
        var conversion = ConvertBody(
            """
            Print #f, SPC(3)
            """);

        conversion.TransformErrors.Should().Contain(e => e.Message.Contains("Print SPC not supported"));
    }

    [TestMethod]
    public void PrintTabProducesTransformError()
    {
        var conversion = ConvertBody(
            """
            Print #f, TAB(3)
            """);

        conversion.TransformErrors.Should().Contain(e => e.Message.Contains("Print TAB not supported"));
    }

    [TestMethod]
    public void CloseStatementCallsDispose()
    {
        var conversion = ConvertBody(
            """
            Close #f
            """);

        GetBodyText(conversion).Should().Contain("Dispose");
    }

    [TestMethod]
    public void LineInputStatementUsesFileSystemLineInput()
    {
        var conversion = ConvertBody(
            """
            Line Input #f, lineText
            """);

        GetBodyText(conversion).Should().Contain("FileSystem.LineInput");
    }

    [TestMethod]
    public void WriteStatementUsesFileSystemWrite()
    {
        var conversion = ConvertBody(
            """
            Write #f, x
            """);

        GetBodyText(conversion).Should().Contain("FileSystem.Write");
    }

    [TestMethod]
    public void KillStatementUsesFileDelete()
    {
        var conversion = ConvertBody(
            """
            Kill filePath
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("File.Delete");
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
