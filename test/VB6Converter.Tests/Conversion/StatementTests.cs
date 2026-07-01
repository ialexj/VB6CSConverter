using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class StatementTests
{
    [TestMethod]
    public void UnloadMeBecomesClose() => ValidateBodyMatches(
        """
        Unload Me
        """,
        """
        Close();
        """);

    [TestMethod]
    public void RaiseEventWithoutArgsUsesEventHandlerPattern() => ValidateBodyMatches(
        """
        RaiseEvent TotalChanged
        """,
        """
        TotalChanged?.Invoke(this, EventArgs.Empty);
        """);

    [TestMethod]
    public void RaiseEventWithArgsMarksUnsupported()
    {
        var conversion = ConvertBody(
            """
            RaiseEvent TotalChanged(1)
            """);

        conversion.TransformErrors.Should().Contain(e => e.Message.Contains("RaiseEvent with arguments not supported"));
    }

    [TestMethod]
    public void ResumeNextBecomesCommentStub()
    {
        var conversion = ConvertBody(
            """
            Resume Next
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Resume Next");
    }

    [TestMethod]
    public void ExitSubBecomesReturn() => ValidateBodyMatches(
        """
        Exit Sub
        """,
        """
        return;
        """);

    [TestMethod]
    public void EndStatementBecomesApplicationExit() => ValidateBodyMatches(
        """
        End
        """,
        """
        Application.Exit();
        """);

    [TestMethod]
    public void BeepStatementBecomesConsoleBeep() => ValidateBodyMatches(
        """
        Beep
        """,
        """
        Console.Beep();
        """);

    static VB6ToCSharpConversion ConvertBody(string vb, string? name = null)
    {
        var wrapper = $"""
        Sub Test()
            {vb}
        End Sub
        """;

        var conversion = VB6ToCSharpConversion.ConvertString(wrapper, name ?? nameof(StatementTests));
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
