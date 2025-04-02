using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

partial class CSharpModelConverter
{
    public class CSharpSelectStatement : CSharpStatement
    {
        public ICSharpExpression Condition { get; set; }

        public List<CSharpSelectCaseStatement> Cases { get; set; } = [];
        
        public List<CSharpStatement> Default { get; internal set; }

        public override string ToString()
        {
            var sb = new StatementBuilder();
            sb.StartBlock($"switch ({Condition}) {{");

            foreach (var @case in Cases) {
                sb.AppendLine(@case.ToString());
            }

            if (Default != null) {
                sb.StartBlock("default:");
                
                foreach (var statement in Default) {
                    sb.AppendLine(statement.ToString());
                }

                sb.EndBlock();
            }

            sb.EndBlock();
            sb.Append("}");
            return sb.ToString();
        }
    }
}
