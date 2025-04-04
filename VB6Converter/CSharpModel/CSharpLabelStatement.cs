namespace VB6Converter.CSharpModel;

class CSharpLabelStatement : ICSharpStatement
{
    public string Name { get; set; }

    public override string ToString() => $"{Name}:";
}