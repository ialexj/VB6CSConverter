using Antlr4.Runtime;
using System.Text;
using System.Text.RegularExpressions;

namespace VB6Parser;

public partial class VisualBasic6Parser
{
    [GeneratedRegex(@"^\s*([\w_]+?\:)\s{2}\s+(.+)$")]
    private static partial Regex LabelWithStatementsOnSameLine();

    [GeneratedRegex(@"\s*[^\w].*?:\s*$")]
    private static partial Regex StatementThatLooksLikeLabel();

    public static StreamReader OpenFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, VisualBasic6Encoding.Encoding);
    }

    public static StartRuleContext Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var reader = new StreamReader(path, VisualBasic6Encoding.Encoding);
        return Parse(reader);
    }

    public static StartRuleContext Parse(TextReader text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Preprocess

        var sb = new StringBuilder();

        string line = null;
        while ((line = text.ReadLine()) != null) {
            // Remove line numbers
            line = line.TrimStart(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);

            if (StatementThatLooksLikeLabel().IsMatch(line)) {
                line = line.TrimEnd(':');
            }

            // Split 
            if (LabelWithStatementsOnSameLine().Match(line) is Match m && m.Success) {
                sb.AppendLine(m.Groups[1].Value);
                sb.AppendLine("        " + m.Groups[2].Value);
            }
            else {
                sb.AppendLine(line);
            }
        }

        string newText = sb.ToString();
        text = new StringReader(newText);

        // Now antlr

        var str = new AntlrInputStream(text);
        var lexer = new VisualBasic6Lexer(str);

        var lexlistener = new ErrorListener<int>();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexlistener);

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        if (lexlistener.HasErrors) {
            throw new ParseException(lexer, tokens, lexlistener.Errors);
        }

        var parselistener = new ErrorListener<IToken>();
        var parser = new VisualBasic6Parser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parselistener);

        var start = parser.startRule();
        if (parselistener.HasErrors) {
            
            throw new ParseException(parser, lexer, tokens, parselistener.Errors);
        }

        return start;
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


[Serializable]
public class ParseException : Exception
{
    public ParseException(IReadOnlyCollection<ParseError> errors) : base(string.Join(Environment.NewLine, errors)) {
        Errors = errors ?? [];
    }

    public ParseException(VisualBasic6Lexer lexer, CommonTokenStream tokens, IReadOnlyCollection<ParseError> errors)
        : this(errors)
    {
        Lexer = lexer;
        Tokens = tokens;
    }

    public ParseException(VisualBasic6Parser parser, VisualBasic6Lexer lexer, CommonTokenStream tokens, IReadOnlyCollection<ParseError> errors)
        : this(lexer, tokens, errors)
    {
        Parser = parser;
    }

    public ParseException(string message) : base(message) { }
    public ParseException(string message, Exception inner) : base(message, inner) { }


    public IReadOnlyCollection<ParseError> Errors { get; }

    public VisualBasic6Parser Parser { get; }

    public VisualBasic6Lexer Lexer { get; }

    public CommonTokenStream Tokens { get; }

}