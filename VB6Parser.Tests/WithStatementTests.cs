using AwesomeAssertions;

namespace VB6Parser.Tests;

[TestClass]
public class WithStatementTests
{
    /// <summary>
    /// Verifies that `Set .member = value` inside a With block is parsed as setStmt,
    /// not letStmt. The SET keyword is also an ambiguousKeyword (usable as identifier),
    /// so if setStmt is not given priority over letStmt in blockStmt alternatives,
    /// `Set` gets treated as an identifier and the statement is misclassified as letStmt.
    /// </summary>
    [TestMethod]
    public void SetInsideWithParsesAsSetStmt()
    {
        const string source =
            """
            Sub Test()
                With a
                    Set .b.c = x
                End With
            End Sub
            """;

        var ctx = VisualBasic6Parser.Parse(new StringReader(source));

        var innerStmt = ctx.Start
            .module().moduleBody().moduleBodyElement(0).subStmt()
            .block().blockStmt(0).withStmt()
            .block().blockStmt(0);

        innerStmt.setStmt().Should().NotBeNull("'Set .b.c = x' should parse as setStmt, not letStmt");
        innerStmt.letStmt().Should().BeNull("'Set .b.c = x' should not be misclassified as letStmt");
    }
}
