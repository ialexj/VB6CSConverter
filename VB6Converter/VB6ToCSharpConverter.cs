using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VB6Converter.CSharpModel;
using VB6Parser;

namespace VB6Converter;

public static class VB6ToCSharpConverter
{
    public static Task<CompilationUnitSyntax> ConvertFile(string inputPath, string outputPath)
    {
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var type = VisualBasicFileTypes.GetFromExtension(inputPath);
        return ConvertFile(inputPath, name, name, type, outputPath);
    }

    public static async Task<CompilationUnitSyntax> ConvertFile(string path, string name, string nsName, VisualBasicFileType type, string output)
    {
        using var reader = VisualBasic6Lexer.OpenFile(path);
        var cu = GetCompilationUnit(reader, name, name, type, output);
        await File.WriteAllTextAsync(output, cu.ToFullString());
        return cu;
    }

    public static string ConvertString(string code, string name, string nsName = null, VisualBasicFileType type = VisualBasicFileType.Module)
    {
        using var reader = new StringReader(code);
        var cu = GetCompilationUnit(reader, name, nsName ?? name, type);
        return cu.ToFullString();
    }

    public static CompilationUnitSyntax GetCompilationUnit(string text, string name, string nsName = null, VisualBasicFileType type = VisualBasicFileType.Module, string outputPath = null)
    {
        using var reader = new StringReader(text);
        return GetCompilationUnit(reader, name, nsName, type, outputPath);
    }

    public static CompilationUnitSyntax GetCompilationUnit(TextReader reader, string name, string nsName, VisualBasicFileType type, string outputPath = null)
    {
        var lexer = VisualBasic6Lexer.Lex(reader, name, outputPath != null ? $"{outputPath}.tokens" : null);
        var parser = VisualBasic6Parser.Parse(lexer, name, outputPath != null ? $"{outputPath}.syntax.lisp" : null);

        if (parser.NumberOfSyntaxErrors > 0) {
            throw new Exception($"error parsing {name}.");
        }

        var transform = new CSharpTransformation();
        return transform.GetCompilationUnit(parser.module(), name, nsName, type);
    }

    public static async Task<Conversion[]> ConvertProject(string path, string[] filter, string outDir)
    {
        var project = VisualBasicProject.Load(path);

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        if (filter != null) {
            project.Files = project.Files.Where(f => filter.Contains(f.Name)).ToList();
        }

        return await Task.WhenAll(
            project.Files.Select(file => Task.Run(
                async () => {
                    try {
                        var cu = await ConvertFile(file.Path, file.Name, project.Name, file.Type, outDir);
                        return new Conversion(file.Name, cu, null);
                    }
                    catch (Exception ex) {
                        return new Conversion(file.Name, null, ex);
                    }
                })));
    }

    public record struct Conversion(string Name, CompilationUnitSyntax CompilationUnit, Exception Error) { }
}
