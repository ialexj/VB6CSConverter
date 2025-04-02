using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using VB6Converter.CSharpModel;

namespace VB6Converter;

public static class Program
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
        var listener_lexer = new ErrorListener<int>("LEXER");
        lexer.AddErrorListener(listener_lexer);

        Console.WriteLine("Lexing...");

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        using (var writer = new StreamWriter($"{output}.tokens")) {
            foreach (var token in tokens.GetTokens()) {
                writer.WriteLine($"{lexer.Vocabulary.GetSymbolicName(token.Type),-15} '{Utils.EscapeWhitespace(token.Text, false)}'");
            }
        }

        if (listener_lexer.had_error) {
            Console.WriteLine("error in lex.");
            return;
        }

        // ----

        Console.WriteLine($"Parsing...");

        var parser = new VisualBasic6Parser(tokens);
        File.WriteAllText($"{output}.lisp", ToStringTree(parser.startRule(), parser.RuleNames));
        tokens.Reset();

        if (parser.NumberOfSyntaxErrors > 0) {
            Console.WriteLine("error in parse.");
            return;
        }       

        Console.WriteLine("Converting...");
        try {
            var converter = new CSharpModelConverter();
            var start = parser.startRule();
            var @class = converter.GetClass(start.module(), "VB6");

            Console.WriteLine($"Outputting...");
            File.WriteAllText(output, @class.ToString());
        }
        catch (NotSupportedException nse) {
            Console.WriteLine($">>>> {nse.Message}");
        }
    }

    //
    // Summary:
    //     Print out a whole tree in LISP form.
    //
    // Remarks:
    //     Print out a whole tree in LISP form. Antlr4.Runtime.Tree.Trees.GetNodeText(Antlr4.Runtime.Tree.ITree,Antlr4.Runtime.Parser)
    //     is used on the node payloads to get the text for the nodes. Detect parse trees
    //     and extract data appropriately.
    public static string ToStringTree(ITree t, IList<string> ruleNames)
    {
        var sb = new StringBuilder();

        void WriteTree(ITree t, IList<string> ruleNames, int indent)
        {
            string indentStr = new(' ', indent * 2);
            string name = Utils.EscapeWhitespace(Trees.GetNodeText(t, ruleNames), escapeSpaces: false);

            if (t.ChildCount == 0) {
                sb.Append($" \"{name}\"");
                return;
            }
            else {
                sb.AppendLine();
                sb.Append(indentStr);
                sb.Append('(');
                sb.Append(name);

                for (int i = 0; i < t.ChildCount; i++) {
                    var child = t.GetChild(i);
                    WriteTree(child, ruleNames, indent + 1);
                }

                if (indent > 0 && EnumerateChildren(t).Any(t => t.ChildCount > 0)) {
                    sb.AppendLine();
                    sb.Append(indentStr);
                }

                sb.Append(')');
            }
        }

        WriteTree(t, ruleNames, 0);
        return sb.ToString();
    }

    static IEnumerable<ITree> EnumerateChildren(this ITree tree)
    {
        for (int i = 0; i < tree.ChildCount; i++) {
            yield return tree.GetChild(i);
        }
    }
}

