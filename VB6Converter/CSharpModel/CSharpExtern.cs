using System.Text;

namespace VB6Converter.CSharpModel;

public class CSharpExtern : ICSharpClassMember
{
    public CSharpVisibility Visibility { get; set; }

    public string Library { get; set; }

    public CSharpType Type { get; set; }

    public string Name { get; set; }

    public string Alias { get; set; }

    public CSharpArgument[] Arguments { get; set; } = [];

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[DllImport({Library}, EntryPoint = \"{Name}\")]");
        sb.AppendLine($"{Visibility.ToCodeString()} static extern {Type} {Alias ?? Name} ({Arguments.ToArgumentsString()});");
        return sb.ToString();
    }
}
