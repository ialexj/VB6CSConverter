using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace VB6Parser;

public partial class VisualBasic6Lexer
{
    public static StreamReader OpenFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, VisualBasic6Encoding.Encoding);
    }

    public static CommonTokenStream Lex(string path, string name, string tokensFilePath = null)
    {
        using var reader = new StreamReader(path, VisualBasic6Encoding.Encoding);
        return Lex(reader, name, tokensFilePath);
    }

    public static CommonTokenStream Lex(TextReader text, string name, string tokensFilePath = null)
    {
        if (text is null) {
            throw new ArgumentNullException(nameof(text));
        }

        var str = new AntlrInputStream(text);
        var lexer = new VisualBasic6Lexer(str);

        var listener_lexer = new ErrorListener<int>("LEXER");
        lexer.AddErrorListener(listener_lexer);

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        if (tokensFilePath != null) {
            using var writer = File.CreateText(tokensFilePath);
            foreach (var token in tokens.GetTokens()) {
                writer.WriteLine($"{lexer.Vocabulary.GetSymbolicName(token.Type),-15} '{Utils.EscapeWhitespace(token.Text, false)}'");
            }
        }

        if (listener_lexer.had_error) {
            throw new Exception($"error lexing {name}.");
        }

        return tokens;
    }
}
