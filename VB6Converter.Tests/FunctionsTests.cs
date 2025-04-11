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
}
