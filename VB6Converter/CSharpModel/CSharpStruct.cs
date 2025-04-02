using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel;

public class CSharpStruct
{
    public CSharpVisibility Visibility { get; set; }

    public string Name { get; set; }

    public CSharpVariable[] Members { get; set; }

    public override string ToString()
    {
        var sb = new StatementBuilder();
        sb.StartBlock($"{Visibility} struct {Name} {{");

        foreach (var member in Members) {
            sb.AppendLine(member.ToString());
        }

        sb.EndBlock("}");
        return sb.ToString();
    }
}
