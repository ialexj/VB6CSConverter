using Antlr4.Runtime;

namespace VB6Parser;

[Serializable]
public class ParseException : Exception
{
    public ParseException(ParseContext parse, IReadOnlyCollection<ParseError> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Context = parse;
        Errors = errors ?? [];
    }

    public ParseException(string message) : base(message) { }

    public ParseException(string message, Exception inner) : base(message, inner) { }


    public ParseContext Context { get; }

    public IReadOnlyCollection<ParseError> Errors { get; }
}