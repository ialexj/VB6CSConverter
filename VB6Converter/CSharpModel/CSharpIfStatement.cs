using System.Collections.Generic;

namespace VB6Converter.CSharpModel
{
    public class CSharpIfStatement : CSharpStatement
    {
        public ICSharpExpression Condition { get; set; }

        public List<CSharpStatement> Then { get; set; } = [];

        public List<CSharpStatement> Else { get; set; } = [];

        public override string ToString()
        {
            var sb = new StatementBuilder();

            sb.StartBlock($"if ({Condition}) {{");

            foreach (var statement in Then) {
                sb.WriteLine(statement.ToString());
            }

            sb.EndBlock();
            sb.Write("}");

            if (Else.Count > 0) {
                sb.Write(" else ");

                if (Else.Count == 1 && Else[0] is CSharpIfStatement @else) {
                    sb.Write(@else.ToString());
                }
                else {
                    sb.StartBlock("{");
                    foreach (var statement in Else) {
                        sb.WriteLine(statement.ToString());
                    }
                    sb.EndBlock();
                    sb.Write("}");
                }
            }

            sb.WriteLine();

            return sb.ToString();
        }
    }
}
