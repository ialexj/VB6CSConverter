using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel
{
    class CSharpWhileStatement : CSharpStatement
    {
        public ICSharpExpression Condition { get; set; } = new CSharpExpression("true");

        public List<CSharpStatement> Statements { get; set; } = [];

        public override string ToString()
        {
            var sb = new StatementBuilder();
            sb.StartBlock($"while ({Condition}) {{");

            foreach (var statement in Statements) {
                sb.AppendLine(statement.ToString());
            }

            sb.EndBlock();
            sb.Append("}");
            return sb.ToString();
        }
    }
}
