using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

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
