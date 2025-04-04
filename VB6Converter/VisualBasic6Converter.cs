using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Antlr4.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VB6Converter.CSharpModel;

namespace VB6Converter;

public static class VisualBasic6Converter
{
    static readonly Encoding VB6Encoding = Encoding.GetEncoding(1252);

    public static async Task ConvertProject(string path, string[] filter, string outDir)
    {
        var project = VisualBasicProject.Load(path);

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        if (filter != null) {
            project.Files = project.Files.Where(f => filter.Contains(f.Name)).ToList();
        }

        var tasks = await Task.WhenAll(
            project.Files.Select(file =>
                Task.Run(() => Convert(file.Path, file.Name, file.Type, outDir))));

        var ns = new CSharpNamespace(project.Name);
        ns.Members.AddRange(tasks.Select(t => t.Result).OfType<CSharpClass>());
        WriteCode(ns, outDir);
    }

    public static void ConvertFile(string path, string outDir)
    {
        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var type = VisualBasicFileTypes.GetFromExtension(path);

        var conversion = Convert(path, name, type, outDir);
        if (conversion.Result is null) {
            return;
        }

        var ns = new CSharpNamespace("VB6Conversion");
        ns.Members.Add(conversion.Result);
        WriteCode(ns, outDir);
    }

    public record struct Conversion(CSharpClass Result, Exception Error) { }

    static Conversion Convert(string path, string name, VisualBasicFileType type, string outDir)
    {
        try {
            Console.WriteLine($"{name} Lexing...");
            var lexer = Lex(path, name, outDir);

            Console.WriteLine($"{name} Parsing...");
            var parser = Parse(lexer, name, outDir);

            Console.WriteLine($"{name} Converting...");
            var @class = ConvertClass(parser, name, type);

            Console.WriteLine($"{name} Converted successfully.");
            return new Conversion(@class, null);
        }
        catch (Exception ex) {
            Console.WriteLine($"{name} Failed converting: {ex.Message}");
            return new Conversion(null, ex);
        }
    }

    static CommonTokenStream Lex(string path, string name, string outDir)
    {
        using var reader = new StreamReader(path, VB6Encoding);

        var str = new AntlrInputStream(reader);
        var lexer = new VB6Parser.VisualBasic6Lexer(str);
        var listener_lexer = new ErrorListener<int>("LEXER");
        lexer.AddErrorListener(listener_lexer);

        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();

        using (var writer = File.CreateText(Path.Join(outDir, $"{name}.tokens"))) {
            foreach (var token in tokens.GetTokens()) {
                writer.WriteLine($"{lexer.Vocabulary.GetSymbolicName(token.Type),-15} '{Utils.EscapeWhitespace(token.Text, false)}'");
            }
        }

        if (listener_lexer.had_error) {
            throw new Exception($"error lexing {name}.");
        }

        return tokens;
    }

    static VB6Parser.VisualBasic6Parser.StartRuleContext Parse(CommonTokenStream tokens, string name, string outputDir)
    {
        var parser = new VB6Parser.VisualBasic6Parser(tokens);
        File.WriteAllText(Path.Combine(outputDir, $"{name}.lisp"), ToStringTree(parser.startRule(), parser.RuleNames));
        tokens.Reset();

        if (parser.NumberOfSyntaxErrors > 0) {
            throw new Exception($"error parsing {name}.");
        }

        return parser.startRule();

        static string ToStringTree(ITree t, IList<string> ruleNames)
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

            static IEnumerable<ITree> EnumerateChildren(ITree tree)
            {
                for (int i = 0; i < tree.ChildCount; i++) {
                    yield return tree.GetChild(i);
                }
            }
        }
    }

    static CSharpClass ConvertClass(VB6Parser.VisualBasic6Parser.StartRuleContext start, string name, VisualBasicFileType type)
    {
        var converter = new CSharpTransformer();

        var c = converter.GetClass(start.module(), name);
        if (type == VisualBasicFileType.Module) {
            c.IsStatic = true;
        }

        return c;
    }

    public static void WriteCode(CSharpNamespace ns, string outDir)
    {
        foreach (var member in ns.Members) {
            File.WriteAllText(Path.Combine(outDir, member.Name + ".cs"), member.ToString());
        }
    }
}
