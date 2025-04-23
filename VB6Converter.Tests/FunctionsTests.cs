using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class FunctionsTests
{
    [TestMethod]
    public void DbOpenTable() => ValidateBodyMatches(
        "Set rsMovimentosCaixa = dbo.OpenRecordset(\"MovimentosCaixa\", dbOpenTable)",
        "rsMovimentosCaixa = dbo.OpenRecordset(\"MovimentosCaixa\", RecordsetTypeEnum.dbOpenTable);"
    );

    [TestMethod]
    public void IIf() => ValidateBodyMatches(
        """
        x = IIf(a, b, c)
        """,
        """
        x = a ? b : c;
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
}
