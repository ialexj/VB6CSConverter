namespace VB6Converter.CSharpModel;

public class CSharpSelectCaseStatement : ICSharpStatement
{
    public ICSharpExpression Condition { get; set; }

    public CSharpBlockStatement Statements { get; set; } = [];

    public override string ToString() => $"case {Condition}: {Statements}";
}
