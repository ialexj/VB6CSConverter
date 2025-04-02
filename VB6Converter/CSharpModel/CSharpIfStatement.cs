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
                sb.AppendLine(statement.ToString());
            }

            sb.EndBlock();
            sb.Append("}");

            if (Else.Count > 0) {
                sb.Append(" else ");

                if (Else.Count == 1 && Else[0] is CSharpIfStatement @else) {
                    sb.Append(@else.ToString());
                }
                else {
                    sb.StartBlock("{");
                    foreach (var statement in Else) {
                        sb.AppendLine(statement.ToString());
                    }
                    sb.EndBlock();
                    sb.Append("}");
                }
            }

            sb.AppendLine();

            return sb.ToString();
        }
    }
}
