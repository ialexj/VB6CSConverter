using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VB6Converter.CSharpModel;

public enum CallType
{
    Unspecified,
    Variable,
    Invoke,
    Array,
    Dictionary
}

public class CSharpCallExpression(CSharpIdentifierExpression target) : ICSharpExpression
{
    public CSharpIdentifierExpression Target { get; set; } = target ?? throw new ArgumentNullException(nameof(target));

    public CSharpCallExpression Callee { get; set; }

    public List<CSharpArgumentCall> Arguments { get; set; } = [];

    public CallType Type { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        
        if (Callee != null) {
            sb.Append(Callee);
            sb.Append('.');
        }

        sb.Append(Target);

        if (Arguments.Count > 0 || Type == CallType.Invoke) {
            sb.Append(Type switch {
                CallType.Array => "[",
                CallType.Dictionary => "[\"",
                _ => "("
            });

            sb.Append(string.Join(", ", Arguments.Select(a => a.ToString())));
            
            sb.Append(Type switch {
                CallType.Array => "]",
                CallType.Dictionary => "\"]",
                _ => ")"
            });
        }

        return sb.ToString();
    }
}

public class CSharpCallStatement(CSharpCallExpression value) : ICSharpStatement
{
    public CSharpCallExpression Expression { get; } = value ?? throw new ArgumentNullException(nameof(value));
    
    public override string ToString() => Expression.ToString() + ';';
}
