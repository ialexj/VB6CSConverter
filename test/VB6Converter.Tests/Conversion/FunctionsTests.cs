using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using System.IO;
using static VB6Converter.Tests.Validations;
using VB6Parser;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class FunctionsTests
{
    [TestMethod]
    public void IIf() => ValidateBodyMatches(
        """
        x = IIf(a, b, c)
        """,
        """
        x = (a ? b : c);
        """);

    [TestMethod]
    public void Array() => ValidateBodyMatches(
        """
        x = Array("a", "b", "c")
        """,
        """
        x = new[]
        {
            "a",
            "b",
            "c"
        };
        """);

    [TestMethod]
    public void Asc() => ValidateBodyMatches(
        """
        x = Asc("a")
        """,
        """
        x = 'a';
        """);

    [TestMethod]
    public void ArrayParameters() => ValidateMemberMatches(
        """
        Private Sub CarregaSubstituicoes(ByVal tipo As Integer, ByRef aSearch() As String, ByRef aReplace() As Integer, ByRef aFormat() As String)
        End Sub
        """,
        """
        private static void CarregaSubstituicoes(int tipo, string[] aSearch, int[] aReplace, string[] aFormat)
        {
        }
        """);

    [TestMethod]
    public void EmitsSourceCommentsWhenRelativePathProvided()
    {
        using var reader = new StringReader("Public Sub Save()\nEnd Sub\n");
        var conversion = VB6ToCSharpConversion.Convert(
            reader,
            className: "Customers",
            nsName: "TestNs",
            type: VisualBasicFileType.Class,
            sourceRelativePath: "vb6/Customers.cls");

        conversion.ParseErrors.Should().BeEmpty();
        conversion.TransformErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();

        var classText = conversion.Class.NormalizeWhitespace().ToFullString();
        classText.Should().Contain("// Generated from: vb6/Customers.cls");
        classText.Should().Contain("// Generated from: vb6/Customers.cls:1");
    }
}
