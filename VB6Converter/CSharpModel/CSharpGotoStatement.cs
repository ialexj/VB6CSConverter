namespace VB6Converter.CSharpModel;

public class CSharpGotoStatement : CSharpStatement
{
    public ICSharpExpression Label { get; set; }

    public override string ToString()
    {
        return $"goto {Label};";
    }
}
