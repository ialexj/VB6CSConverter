using System.Linq;

namespace VB6Converter.CSharpModel;

class CSharpOperatorExpression(string oper, ICSharpExpression[] expressions) : ICSharpExpression
{
    public string Operator { get; } = oper;

    public ICSharpExpression[] Expressions { get; } = expressions;

    public override string ToString() => string.Join(Operator, Expressions.Select(e => e.ToString()));
}
