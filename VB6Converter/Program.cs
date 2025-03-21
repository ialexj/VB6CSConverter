using Antlr4.Runtime;
using System;
using System.Diagnostics;
using System.IO;
using VB6Converter.CSharpModel;

namespace VB6Converter;

public class Program
{
    static void Main(string[] args)
    {
        Convert("Input.txt", "Output.cs");
    }

    static void Convert(string input, string output)
    {
        Trace.Listeners.Add(new ConsoleTraceListener() {
            Filter = new EventTypeFilter(SourceLevels.Warning)
        });

        using var stream = File.OpenRead(input);
        
        var str = new AntlrInputStream(stream);
        var lexer = new VisualBasic6Lexer(str);
        var listener_lexer = new ErrorListener<int>();
        lexer.AddErrorListener(listener_lexer);

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        Console.WriteLine("\nTokens:");

        foreach (var token in tokens.GetTokens()) {
            Console.WriteLine($"  {lexer.Vocabulary.GetSymbolicName(token.Type),-15} '{token.Text}'");
        }

        var parser = new VisualBasic6Parser(tokens) {
            BuildParseTree = true
        };
        
        Console.WriteLine($"Parse tree: {parser.startRule().ToStringTree(parser)}");
        tokens.Reset();

        var listener_parser = new ErrorListener<IToken>();
        parser.AddErrorListener(listener_parser);

        Console.WriteLine($"Parsing...");
        var start = parser.startRule();
        if (listener_lexer.had_error || listener_parser.had_error) {
            Console.WriteLine("error in parse.");
            return;
        }

        Console.WriteLine("Converting...");
        try {

            var converter = new CSharpModelConverter();
            var @class = converter.GetClass(start.module(), "VB6");


            Console.WriteLine($"Outputting...");
            File.WriteAllText(output, @class.ToString());
        }
        catch (NotSupportedException nse) {
            Console.WriteLine($">>>> {nse.Message}");
        }
    }
}
