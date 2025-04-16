using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Conversion;
class TraceMethod : IDisposable
{
    public TraceMethod(object ctx, [CallerMemberName] string procedure = null)
    {
#if DEBUG
        //Trace.TraceInformation($"{procedure}({ctx?.GetType().Name}) \"{Utils.EscapeWhitespace((ctx as ParserRuleContext)?.GetText() ?? string.Empty, false)}\"");
        //Trace.Indent();
#endif
    }

    public void Dispose()
    {
#if DEBUG
        //Trace.Unindent();
#endif
    }
}
