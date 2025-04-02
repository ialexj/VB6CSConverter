using System.Collections.Generic;

namespace VB6Converter.CSharpModel
{
    public abstract class CSharpStatement { }

    public class CSharpBlockStatement(List<CSharpStatement> statements) : CSharpStatement
    {
        public List<CSharpStatement> Statements { get; } = statements;

        public override string ToString()
        {
            var b = new StatementBuilder();

            b.StartBlock("{");
            foreach (var statement in Statements) {
                b.AppendLine(statement.ToString());
            }
            b.EndBlock("}");

            return b.ToString();
        }
    }

    public class CSharpGenericStatement(string text) : CSharpStatement
    {
        public string Text { get; } = text;

        public override string ToString() => Text;
    }
}
