// Template generated code from Antlr4BuildTasks.Template v 8.17
using Antlr4.Runtime;

namespace VB6Parser;

public record struct ParseError(string Message, int Line, int Col) 
{
    public override string ToString() => Message;
}