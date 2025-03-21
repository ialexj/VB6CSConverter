using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.CSharpModel
{
    public record class CSharpArgument(CSharpVariable Variable, ICSharpExpression DefaultValue = null)
    {
        public override string ToString()
        {
            return Variable + (DefaultValue != null ? $" = {DefaultValue}" : "");
        }
    }

    public static class CSharpArgumentsExtensions
    {
        public static string ToArgumentsString(this IEnumerable<CSharpArgument> arguments)
        {
            return string.Join(", ", arguments.Select(a => a.ToString()));
        }
    }
}
