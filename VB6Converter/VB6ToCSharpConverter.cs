using Antlr4.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        using var reader = VisualBasic6Parser.OpenFile(path);
        var cu = GetCompilationUnit(reader, name, nsName, type, output);
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
        var parse = VisualBasic6Parser.Parse(reader);

        if (parse.HasErrors && outputPath != null) {
            parse.WriteTokens($"{outputPath}.tokens", true);
            parse.WriteTree($"{outputPath}.syntax.lisp", true);
            parse.WriteErrors($"{outputPath}.log", true);
            throw new Exception("Parse errors found. See log file for details.");
        }

        var transform = new VB6ToCSharpTransformation(failOnError: false);
        return transform.GetCompilationUnit(parse.Parser.module(), name, nsName, type == VisualBasicFileType.Module);
    }

    public static async Task<Conversion[]> ConvertProject(string path, string[] filter, string outDir)
    {
        var project = VisualBasicProject.Load(path);

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        var files = filter != null ? project.Files.Where(f => filter.Contains(f.Name)) : project.Files;

        var conversions = await Task.WhenAll(
            files.Select(file => Task.Run(
                async () => {
                    try {
                        Console.WriteLine($"{file.Name} Converting...");
                        var output = Path.Combine(outDir, $"{file.Name}.cs");
                        var cu = await ConvertFile(file.Path, file.Name, project.Name, file.Type, output);
                        Console.WriteLine($"{file.Name} Converted.");
                        return new Conversion(file.Name, cu, null);
                    }
                    catch (Exception ex) when (!Debugger.IsAttached) {
                        Console.WriteLine($"{file.Name} ERROR: {ex.Message}");
                        return new Conversion(file.Name, null, ex);
                    }
                })));

        //StringBuilder globalUsings = new();
        //foreach (var file in files) {
        //    globalUsings.AppendLine($"global using static {project.Name}.{file.Name};");
        //}

        //var globalUsingsPath = Path.Combine(outDir, "_VB6Usings.cs");
        //File.WriteAllText(globalUsingsPath, globalUsings.ToString());

        string projectPath = Path.Combine(outDir, $"{project.Name}.csproj");
        if (!File.Exists(projectPath)) {
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>latest</LangVersion>
                  </PropertyGroup>
                </Project>
                """);
        }

        return conversions;
    }

    public record struct Conversion(string Name, CompilationUnitSyntax CompilationUnit, Exception Error) { }
}
