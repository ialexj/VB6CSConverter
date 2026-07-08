using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class NewTests
{
    [TestMethod]
    public void New() => ValidateBodyMatches(
        """
        Set x = new Database
        """,
        """
        // Set
        x = new Database();
        """);
}
