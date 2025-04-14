using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Text;

namespace VB6Parser;

public partial class VisualBasic6Parser
{
    [GeneratedRegex(@"(.+?\:)\s{2}\s+(.+)")]
    private static partial Regex WeirdMultilineRegex();

    public static StreamReader OpenFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, VisualBasic6Encoding.Encoding);
    }

    public static ParseResult Parse(string path)
    {
        using var reader = new StreamReader(path, VisualBasic6Encoding.Encoding);
        return Parse(reader);
    }

    public static ParseResult Parse(TextReader text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Preprocess

        var sb = new StringBuilder();

        string line = null;
        while ((line = text.ReadLine()) != null) {
            if (WeirdMultilineRegex().Match(line) is Match m && m.Success) {
                sb.AppendLine(m.Groups[1].Value);
                sb.AppendLine(m.Groups[2].Value);
            }
            else {
                sb.AppendLine(line);
            }
        }

        text = new StringReader(sb.ToString());

        // Now antlr

        var str = new AntlrInputStream(text);
        var lexer = new VisualBasic6Lexer(str);

        var lexlistener = new ErrorListener<int>();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexlistener);

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        if (lexlistener.HasErrors) {
            return new ParseResult(null, lexer, tokens, lexlistener.Errors);
        }

        var parselistener = new ErrorListener<IToken>();
        var parser = new VisualBasic6Parser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parselistener);

        return new ParseResult(parser, lexer, tokens, parselistener.Errors);
    }   
}

public record struct ParseResult(VisualBasic6Parser Parser, VisualBasic6Lexer Lexer, CommonTokenStream Tokens, IReadOnlyCollection<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;

    public void WriteTokens(string file, bool replace = false)
    {
        using var writer = replace ? File.CreateText(file) : File.AppendText(file);
        WriteTokens(writer);
    }

    public void WriteTokens(TextWriter writer)
    {
        foreach (var token in Tokens.GetTokens()) {
            writer.WriteLine($"{Lexer.Vocabulary.GetSymbolicName(token.Type),-15} '{Utils.EscapeWhitespace(token.Text, false)}'");
        }
    }

    public void WriteTree(string file, bool replace = false)
    {
        using var writer = replace ? File.CreateText(file) : File.AppendText(file);
        WriteTree(writer);
    }

    public void WriteTree(TextWriter writer)
    {
        var index = Tokens.Index;
        Tokens.Reset();
        writer.Write(TreeUtils.ToStringTree(Parser.startRule(), Parser.RuleNames));
        Tokens.Seek(index);
    }

    public void WriteErrors(string file, bool replace = false)
    {
        using var writer = replace ? File.CreateText(file) : File.AppendText(file);
        WriteErrors(writer);
    }

    public void WriteErrors(TextWriter writer)
    {
        foreach (var error in Errors) {
            writer.WriteLine(error);
        }
    }
}