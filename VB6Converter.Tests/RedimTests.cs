using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

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
}
