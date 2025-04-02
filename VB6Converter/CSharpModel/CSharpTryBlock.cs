using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

public class CSharpTryBlock : CSharpStatement
{
    public string CatchLabel { get; set; }
    public List<CSharpStatement> TryStatements { get; set; } = [];

    public List<CSharpStatement> CatchStatements { get; set; } = [];

    public override string ToString()
    {
        var sb = new StatementBuilder();

        sb.StartBlock("try {");

        foreach (var statement in TryStatements) {
            sb.AppendLine(statement.ToString());
        }

        sb.EndBlock("}");
        sb.StartBlock("catch {");

        foreach (var statement in CatchStatements) {
            sb.AppendLine(statement.ToString());
        }

        sb.EndBlock("}");
        return sb.ToString();
    }
}
