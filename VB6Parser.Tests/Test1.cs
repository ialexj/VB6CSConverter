using FluentAssertions;

namespace VB6Parser.Tests
{
    [TestClass]
    public sealed class Test1
    {
        [TestMethod]
        public void LineLabel()
        {
            var vb = """
                Erro:
                Exit Sub
                """;

            var parse = VisualBasic6Parser.Parse(new StringReader(vb));
            var block = parse.Parser.block();
            var statements = block.blockStmt();

            statements.Should().HaveCount(2);
            statements[0].lineLabel().Should().NotBeNull();
        }
    }
}
