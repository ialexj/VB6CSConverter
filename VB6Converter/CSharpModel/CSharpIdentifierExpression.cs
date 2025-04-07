namespace VB6Converter.CSharpModel;

public record class CSharpIdentifierExpression(string Name, CSharpType CastType = default, IdentifierType IdentifierType = IdentifierType.Unknown) : ICSharpExpression
{
    public override string ToString()
    {
        if (CastType != CSharpType.Unknown) {
            return $"({CastType}) {Name}";
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