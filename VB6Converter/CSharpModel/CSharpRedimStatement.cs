using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VB6Converter.CSharpModel
{
    public class CSharpRedimStatement : ICSharpStatement
    {
        public CSharpCallExpression Variable { get; internal set; }
        
        public CSharpType Type { get; internal set; }

        public List<ICSharpExpression> Subscripts { get; internal set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{Variable} = new {Type}");

            var subscripts = string.Join(", ", Subscripts.Select(s => $"({s} + 1)"));
            sb.Append($"[{subscripts}]");

            return sb.ToString();
        }
    }
}
