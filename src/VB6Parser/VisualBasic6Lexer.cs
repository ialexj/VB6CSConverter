using Antlr4.Runtime.Misc;

namespace VB6Parser;

public partial class VisualBasic6Lexer
{
    public void WriteTokens(string file, bool replace = false)
    {
        using var writer = replace ? File.CreateText(file) : File.AppendText(file);
        WriteTokens(writer);
    }

    public void WriteTokens(TextWriter writer)
    {
        foreach (var token in GetAllTokens()) {
            writer.WriteLine($"{Vocabulary.GetSymbolicName(token.Type),-15} '{Utils.EscapeWhitespace(token.Text, false)}'");
        }
    }
}
