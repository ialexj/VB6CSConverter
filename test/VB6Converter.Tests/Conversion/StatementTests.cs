using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using VB6Converter.Conversion;
using VB6Converter.Rewriters;
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
        Unload(this);
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
    public void SetAssignmentIsMarkedForLaterDefaultMemberExpansion()
    {
        var conversion = ConvertBody(
            """
            Set x = Nothing
            """);

        var statement = conversion.Class.Members.OfType<MethodDeclarationSyntax>().Single().Body!.Statements.Single();
        statement.IsSetAssignment().Should().BeTrue();
    }

    [TestMethod]
    public void LetAssignmentIsNotMarkedAsSet()
    {
        var conversion = ConvertBody(
            """
            x = 1
            """);

        var statement = conversion.Class.Members.OfType<MethodDeclarationSyntax>().Single().Body!.Statements.Single();
        statement.IsSetAssignment().Should().BeFalse();
    }

    [TestMethod]
    public void DimAsNewUsesObjectInitializer() => ValidateBodyMatches(
        """
        Dim col As New Collection
        """,
        """
        Microsoft.VisualBasic.Collection col = new();
        """, new VBCoreRewriter());

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
        System.Windows.Forms.Application.Exit();
        """);

    [TestMethod]
    public void BeepStatementBecomesConsoleBeep() => ValidateBodyMatches(
        """
        Beep
        """,
        """
        System.Console.Beep();
        """);

    [TestMethod]
    public void IfElseIfPreservesFirstBranch() => ValidateBodyMatches(
        """
        If Len(!agnTelefone) > 0 Then
            SMS_EnviaNumero !agnTelefone
        ElseIf !agnIDCliente > 0 Then
            SMS_EnviaCliente !agnIDCliente
        Else
            MsgBox "A marcação não está associada a um cliente.", vbOKOnly + vbInformation, "Enviar SMS Marcação"
        End If
        """,
        """
        if (Len((string)this["agnTelefone"]) > 0)
        {
            SMS_EnviaNumero(this["agnTelefone"]);
        }
        else if (this["agnIDCliente"] > 0)
        {
            SMS_EnviaCliente(this["agnIDCliente"]);
        }
        else
        {
            Microsoft.VisualBasic.Interaction.MsgBox("A marcação não está associada a um cliente.", Microsoft.VisualBasic.Constants.vbOKOnly | Microsoft.VisualBasic.Constants.vbInformation, "Enviar SMS Marcação");
        }
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
