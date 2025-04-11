using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class NewTests
{
    [TestMethod]
    public void New() => ValidateBodyMatches(
        """
        Set x = new Database
        """,
        """
        x = new Database();
        """);
}
