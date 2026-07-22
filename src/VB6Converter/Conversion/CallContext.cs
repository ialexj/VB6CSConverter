using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VB6Parser;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

public readonly record struct CallContext
{
    public CallContext(ConversionOptions options = null, params ICallContext[] withStack)
    {
        Options = options;
        WithStack = withStack ?? [];
    }

    public ConversionOptions Options { get; }

    public ICallContext[] WithStack { get; }

    public ICallContext With => WithStack.LastOrDefault();

    public CallContext PushWith(ICallContext with)
    {
        if (with is null) {
            return this;
        }

        ICallContext[] stack = with.IsPartial && WithStack.Length > 0
            ? [.. WithStack, with]
            : [with];

        return new CallContext(Options, stack);
    }
}

public readonly record struct ClassContext(
    string Name,
    bool Static,
    ConversionOptions Options = null,
    string SourceDirectory = null,
    string OutputDirectory = null,
    string SourceRelativePath = null)
{
    public bool UseDynamic => Options?.UseDynamic ?? true;
}
