using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VB6Parser;

namespace VB6Converter;

public static partial class VB6ToCSharpConverter
{
    public static VB6ToCSharpConversion ConvertFile(string input, string output)
    {
        var name = Path.GetFileNameWithoutExtension(input);
        var type = VisualBasicFileTypes.GetFromExtension(input);
        return ConvertFile(input, output, name, name, type);
    }

    public static VB6ToCSharpConversion ConvertFile(string input, string output, string name, string nsName, VisualBasicFileType type)
    {
        try {
            using var reader = VisualBasic6Parser.OpenFile(input);
            var start = VisualBasic6Parser.Parse(reader);

            File.Delete($"{output}.log");
            File.Delete($"{output}.tokens");
            File.Delete($"{output}.lisp");

            var cu = GetConversion(start, name, nsName, type);

            File.WriteAllText(output, cu.CompilationUnit.ToFullString());
            return cu;
        }
        catch (ParseException parse) {
            File.WriteAllText($"{output}.log", parse.Message);
            parse.Lexer?.WriteTokens($"{output}.tokens", true);
            parse.Parser?.WriteTree($"{output}.syntax.lisp", true);
            throw;
        }
    }

    public static VB6ToCSharpConversion ConvertString(string vb6CodeString, string name, string nsName = null, VisualBasicFileType type = VisualBasicFileType.Module)
    {
        using var reader = new StringReader(vb6CodeString);
        var start = VisualBasic6Parser.Parse(reader);
        return GetConversion(start, name, nsName, type);
    }

    static VB6ToCSharpConversion GetConversion(VisualBasic6Parser.StartRuleContext start, string name, string nsName, VisualBasicFileType type)
        => new VB6ToCSharpConversion(
            start.module(), name, nsName,
            type == VisualBasicFileType.Module);

    public static CSharpCompilation GetCompilation(IEnumerable<CompilationUnitSyntax> files)
    {
        var references = new MetadataReference[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        return CSharpCompilation.Create(
            "Project", files.Select(cu => cu.SyntaxTree), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
