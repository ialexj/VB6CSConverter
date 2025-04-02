namespace VB6Converter.CSharpModel;

public record class CSharpIdentifierExpression(string Name, CSharpType Type = default) : ICSharpExpression
{
    public override string ToString()
    {
        if (Type != CSharpType.Unknown) {
            return $"({Type}) {Name}";
        }
        else {
            return Name;
        }
    }
}