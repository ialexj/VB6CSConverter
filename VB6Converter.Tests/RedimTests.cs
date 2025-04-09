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

    // Redim without type to be fixed later with the semantic model
    // Redim preserve tbd
}
