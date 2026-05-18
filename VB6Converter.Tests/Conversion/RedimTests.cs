using static VB6Converter.Tests.Validations;
using AwesomeAssertions;

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
        arr = new string[10, 10, 10];
        """);

    [TestMethod]
    public void RedimPreserve() => ValidateBodyMatches(
        """
        ReDim Preserve arr(10) As String
        """,
        """
        Array.Resize(ref arr, 10);
        """);

    [TestMethod]
    public void RedimWithoutTypeDefaultsToObject() => ValidateBodyMatches(
        """
        ReDim arr(10)
        """,
        """
        arr = new object[10];
        """);

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
