using System;
using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.CSharpModel;

public class CSharpCallValue : ICSharpExpression
{
    public enum CallType
    {
        Variable,
        Method,
        Array,
        Dictionary
    }

    public ICSharpExpression Callee { get; set; }

    public List<ICSharpExpression> Members { get; set; } = [];

    public List<CSharpArgumentCall> Arguments { get; set; } = [];

    public CallType Type { get; internal set; }

    public override string ToString()
    {
        string target = string.Empty;
        if (Callee != null && Members.Count > 0) {
            target = $"{Callee}.{string.Join(".", Members)}";
        }
        else if (Callee != null) {
            target = Callee.ToString();
        }

        string arguments = string.Empty;

        if (Type != CallType.Variable) {
            string open = Type switch {
                CallType.Method => "(",
                CallType.Array => "[",
                CallType.Dictionary => "[\"",
                _ => ""
            };

            string close = Type switch {
                CallType.Method => ")",
                CallType.Array => "]",
                CallType.Dictionary => "\"]",
                _ => ""
            };

            var args = string.Join(", ", Arguments.Select(a => a.ToString()));

            arguments = open + args + close;
        }

        return target + arguments;
    }
}

public class CSharpCallStatement(CSharpCallValue value) : CSharpStatement
{
    public CSharpCallValue Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
    
    public override string ToString() => Value.ToString() + ';';
}
