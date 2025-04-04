using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel
{
    public class CSharpPrintStatement : ICSharpStatement
    {
        public ICSharpExpression Target { get; set; }

        public List<ICSharpExpression> Outputs { get; set; } = [];

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Target);
            sb.Append(".Write(");

            foreach (var output in Outputs) {
                sb.Append(", ");
                sb.Append(output);
            }

            sb.Append(')');
            return sb.ToString();
        }
    }
}
