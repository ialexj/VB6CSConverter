using Antlr4.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VB6Parser;

namespace VB6Converter;

public static partial class VB6ToCSharpConverter
{
    public static VB6ToCSharpConversion ConvertFile(string inputPath, string outputPath)
    {
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var type = VisualBasicFileTypes.GetFromExtension(inputPath);
        return ConvertFile(inputPath, name, name, type, outputPath);
    }

    public static VB6ToCSharpConversion ConvertFile(string path, string name, string nsName, VisualBasicFileType type, string output)
    {
        using var reader = VisualBasic6Parser.OpenFile(path);
        var cu = GetConversion(reader, name, nsName, type, output);
        File.WriteAllText(output, cu.CompilationUnit.ToFullString());
        return cu;
    }

    public static string ConvertString(string code, string name, string nsName = null, VisualBasicFileType type = VisualBasicFileType.Module)
    {
        using var reader = new StringReader(code);
        var cu = GetConversion(reader, name, nsName ?? name, type);
        return cu.CompilationUnit.ToFullString();
    }

    public static VB6ToCSharpConversion GetConversion(string text, string name, string nsName = null, VisualBasicFileType type = VisualBasicFileType.Module, string outputPath = null)
    {
        using var reader = new StringReader(text);
        return GetConversion(reader, name, nsName, type, outputPath);
    }

    public static VB6ToCSharpConversion GetConversion(TextReader reader, string name, string nsName, VisualBasicFileType type, string outputPath = null)
    {
        var parse = VisualBasic6Parser.Parse(reader);

        if (parse.HasErrors && outputPath != null) {
            parse.WriteTokens($"{outputPath}.tokens", true);
            parse.WriteTree($"{outputPath}.syntax.lisp", true);
            parse.WriteErrors($"{outputPath}.log", true);
            throw new Exception("Parse errors found. See log file for details.");
        }

        return new VB6ToCSharpConversion(
            parse.Parser.module(), name, nsName,
            type == VisualBasicFileType.Module,
            failOnError: false);
    }

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
