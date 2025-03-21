using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

partial class CSharpModelConverter
{
    public class CSharpSelectCaseStatement : CSharpStatement
    {
        public ICSharpExpression Condition { get; set; }

        public List<CSharpStatement> Statements { get; set; } = [];

        public override string ToString()
        {
            var sb = new StatementBuilder();

            sb.StartBlock($"case {Condition}:");
            foreach (var statement in Statements) {
                sb.WriteLine(statement.ToString());
            }

            sb.EndBlock();
            return sb.ToString();
        } 
    }
}
