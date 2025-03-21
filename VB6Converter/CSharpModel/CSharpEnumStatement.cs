using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VB6Converter.CSharpModel;

partial class CSharpModelConverter
{
    class CSharpEnumStatement
    {
        public CSharpVisibility Visibility { get; set; }

        public string Name { get; set; }

        public List<(string name, CSharpExpression value)> Values { get; set; } = [];

        public override string ToString()
        {
            var sb = new StatementBuilder();

            sb.Write(Visibility.ToCodeString());
            sb.Write(" enum ");
            sb.Write(Name);
            sb.StartBlock(" {");

            var values = Values.Select(v => {
                var b = new StringBuilder();
                b.Append(v.name);
                if (v.value != null) {
                    b.Append(" = ");
                    b.Append(v.value.ToString());
                }
                return b.ToString();
            });

            sb.WriteLine(string.Join("," + Environment.NewLine));
            sb.EndBlock("}");

            return sb.ToString();
        }
    }
}
