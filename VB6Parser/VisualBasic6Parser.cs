using Antlr4.Runtime;
using System.Text;
using System.Text.RegularExpressions;

namespace VB6Parser;

public partial class VisualBasic6Parser
{

    public static StreamReader OpenFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, VisualBasic6Encoding.Encoding);
    }

    public static ParseContext Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var reader = new StreamReader(path, VisualBasic6Encoding.Encoding);
        return Parse(reader);
    }

    public static ParseContext Parse(TextReader text, string name = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Pre-processing
        string source = Preprocessor.Preprocess(text);

        // Lexing
        var str = new AntlrInputStream(source) { name = name };
        var lexer = new VisualBasic6Lexer(str);

        var lexlistener = new ErrorListener<int>();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexlistener);

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        if (lexlistener.HasErrors) {
            throw new ParseException(new ParseContext(tokens, lexer, source), lexlistener.Errors);
        }

        // Parsing
        var parselistener = new ErrorListener<IToken>();
        var parser = new VisualBasic6Parser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parselistener);

        var start = parser.startRule();
        if (parselistener.HasErrors) {
            throw new ParseException(new ParseContext(parser, tokens, lexer, source), parselistener.Errors);
        }

        return new ParseContext(start, parser, tokens, lexer, source);
    }

    public void WriteTree(string file, bool replace = false)
    {
        using var writer = replace ? File.CreateText(file) : File.AppendText(file);
        WriteTree(writer);
    }

    public void WriteTree(TextWriter writer)
    {
        var index = TokenStream.Index;
        TokenStream.Seek(0);
        writer.Write(TreeUtils.ToStringTree(startRule(), RuleNames));
        TokenStream.Seek(index);
    }
}
