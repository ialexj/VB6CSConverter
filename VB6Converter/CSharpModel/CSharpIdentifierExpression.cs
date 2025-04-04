namespace VB6Converter.CSharpModel;

public record class CSharpIdentifierExpression(string Name, CSharpType Type = default, IdentifierType IdentifierType = IdentifierType.Unknown) : ICSharpExpression
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

public enum IdentifierType
{
    Unknown,
    Variable,
    Method,
    Array,
    Dictionary
}