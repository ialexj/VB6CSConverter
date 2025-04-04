using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

public class CSharpNamespace(string name)
{
    public string Name { get; } = name;

    public List<CSharpNamespaceMember> Members { get; set; } = [];

    public override string ToString()
    {
        var b = new StatementBuilder();
        b.StartBlock($"namespace {Name} {{");

        foreach (var m in Members) {
            b.AppendLine(m.ToString());
        }

        b.EndBlock("}");
        return b.ToString();
    }
}

public abstract class CSharpNamespaceMember(CSharpVisibility visibility, string name)
{
    public CSharpVisibility Visibility { get; set; } = visibility;

    public string Name { get; set; } = name;


    protected abstract string GetDeclaration();

    protected abstract IEnumerable<object> GetBody();

    public override string ToString()
    {
        var sb = new StatementBuilder();
        sb.StartBlock($"{Visibility.ToCodeString()} {GetDeclaration()} {Name} {{");

        foreach (var b in GetBody()) {
            sb.AppendLine(b.ToString());
        }

        sb.EndBlock("}");
        return sb.ToString();
    }
}
