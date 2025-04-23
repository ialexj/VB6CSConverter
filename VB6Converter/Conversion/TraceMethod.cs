
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Serilog;
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
        Log.Default.Verbose("{method}({context}) \"{text}\"", 
            procedure, ctx?.GetType().Name,
            Utils.EscapeWhitespace((ctx as ParserRuleContext)?.GetText() ?? string.Empty, false));
    }

    public void Dispose()
    {
    }
}
