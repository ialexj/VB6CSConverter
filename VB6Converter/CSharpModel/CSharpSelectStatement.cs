using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

public class CSharpSelectStatement : ICSharpStatement
{
    public ICSharpExpression Condition { get; set; }

    public List<CSharpSelectCaseStatement> Cases { get; set; } = [];
        
    public CSharpBlockStatement Default { get; internal set; }

    public override string ToString()
    {
        var sb = new StatementBuilder();
        sb.StartBlock($"switch ({Condition}) {{");

        foreach (var @case in Cases) {
            sb.AppendLine(@case.ToString());
        }

        if (Default != null) {
            sb.Append($"default: {Default}");
        }

        sb.EndBlock();
        sb.Append("}");
        return sb.ToString();
    }
}
