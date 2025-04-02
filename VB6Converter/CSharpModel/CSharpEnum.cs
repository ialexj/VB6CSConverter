using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VB6Converter.CSharpModel;

public class CSharpEnum
{
    public string Name { get; set; }

    public CSharpVisibility Visibility { get; set; }

    public List<(string name, CSharpExpression value)> Values { get; set; } = [];

    public override string ToString()
    {
        var sb = new StatementBuilder();

        sb.Append(Visibility.ToCodeString());
        sb.Append(" enum ");
        sb.Append(Name.ToString());
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

        sb.AppendLine(string.Join("," + Environment.NewLine));
        sb.EndBlock("}");

        return sb.ToString();
    }
}