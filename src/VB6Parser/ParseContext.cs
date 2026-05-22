using Antlr4.Runtime;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser;

public record struct ParseContext(StartRuleContext Start, VisualBasic6Parser Parser, CommonTokenStream Tokens, VisualBasic6Lexer Lexer, string Source) 
{
    public ParseContext(VisualBasic6Parser Parser, CommonTokenStream Tokens, VisualBasic6Lexer Lexer, string Source)
        : this(null, Parser, Tokens, Lexer, Source)
    {
    }

    public ParseContext(CommonTokenStream Tokens, VisualBasic6Lexer Lexer, string Source)
        : this(null, null, Tokens, Lexer, Source)
    {
    }
}
