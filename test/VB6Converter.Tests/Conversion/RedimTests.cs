using static VB6Converter.Tests.Validations;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class RedimTests
{
    [TestMethod]
    public void Redim() => ValidateBodyMatches(
        """
        ReDim arr(10, 10, 10) As String
        """,
        """
        arr = new string[10 + 1, 10 + 1, 10 + 1];
        """);

    [TestMethod]
    public void RedimPreserve() => ValidateBodyMatches(
        """
        ReDim Preserve arr(10) As String
        """,
        """
        System.Array.Resize(ref arr, 10 + 1);
        """);

    [TestMethod]
    public void RedimWithoutTypeDefaultsToObject() => ValidateBodyMatches(
        """
        ReDim arr(10)
        """,
        """
        arr = new dynamic[10 + 1];
        """);

    [TestMethod]
    public void RedimWithExplicitZeroLowerBoundsUsesSimpleArrayCreation() => ValidateBodyMatches(
        """
        ReDim aRegistos(0 To lRegistos, 0 To LIN_VALOR_IVA)
        """,
        """
        aRegistos = new dynamic[lRegistos + 1, LIN_VALOR_IVA + 1];
        """);

    [TestMethod]
    public void RedimWithNonZeroMultiDimensionalLowerBoundsUsesCreateInstance() => ValidateBodyMatches(
        """
        ReDim aRegistos(0 To lRegistos, LIN_ARTIGO_ID To LIN_VALOR_IVA)
        """,
        """
        aRegistos = (dynamic[, ])System.Array.CreateInstance(typeof(object), new int[] { (lRegistos) - (0) + 1, (LIN_VALOR_IVA) - (LIN_ARTIGO_ID) + 1 }, new int[] { 0, LIN_ARTIGO_ID });
        """);

    [TestMethod]
    public void RedimSingleDimensionNonZeroLowerBoundProducesWarning()
    {
        var conversion = VB6ToCSharpConversion.ConvertString(
            """
            Sub Test()
                ReDim arr(5 To 10) As String
            End Sub
            """,
            nameof(RedimSingleDimensionNonZeroLowerBoundProducesWarning));

        conversion.ParseErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();
        conversion.TransformErrors.Should().Contain(e => e.Message.Contains("Non-zero lower bound on single-dimensional array is not honored"));
        conversion.Class.NormalizeWhitespace().ToFullString().Should().Contain("arr = // ERROR: Non-zero lower bound on single-dimensional array is not honored");
        conversion.Class.NormalizeWhitespace().ToFullString().Should().Contain("new string[10 + 1];");
    }

    [TestMethod]
    public void RedimPreserveSingleDimensionNonZeroLowerBoundProducesWarning()
    {
        var conversion = VB6ToCSharpConversion.ConvertString(
            """
            Sub Test()
                ReDim Preserve arr(5 To 10) As String
            End Sub
            """,
            nameof(RedimPreserveSingleDimensionNonZeroLowerBoundProducesWarning));

        conversion.ParseErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();
        conversion.TransformErrors.Should().Contain(e => e.Message.Contains("Non-zero lower bound on single-dimensional array is not honored"));
        conversion.Class.NormalizeWhitespace().ToFullString().Should().Contain("System.Array.Resize(ref arr, 10 + 1);");
    }

    [TestMethod]
    public void RedimPreserveMultiDimensionProducesTransformError()
    {
        var conversion = VB6ToCSharpConversion.ConvertString(
            """
            Sub Test()
                ReDim Preserve arr(1, 2) As String
            End Sub
            """,
            nameof(RedimPreserveMultiDimensionProducesTransformError));

        conversion.ParseErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();
        conversion.TransformErrors.Should().Contain(e => e.Message.Contains("Multi-dimensional Redim Preserve not supported"));
    }
}
