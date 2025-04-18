using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VB6Converter.Conversion;
using VB6Parser;
using static VB6Converter.ConsoleColors;

namespace VB6Converter;

public class CommandLineOptions
{
    [Option('p', "project", Required = true, HelpText = "Path to the VB6 project file.")]
    public string Project { get; set; }

    [Option('o', "output", Required = true, HelpText = "Output directory for the converted files.")]
    public string Output { get; set; }

    [Option('u', "update", Required = false, HelpText = "Files to update if already converted.")]
    public IEnumerable<string> Update { get; set; } = [];

    [Option('f', "filter", Required = false, HelpText = "Only process the specified files.")]
    public IEnumerable<string> Filter { get; set; } = [];

    [Option("show-output", Required = false, HelpText = "Print the converted file to the console.")]
    public bool Show { get; set; } = false;
}

public static class Program
{
    public static Task Main(string[] args) => Run(Parser.Default.ParseArguments<CommandLineOptions>(args).Value);

    static async Task Run(CommandLineOptions options)
    {
        var project = VisualBasicProject.Load(options.Project);
        var outDir = options.Output;

        var targets = project.Files.Select(f => ConversionTarget.Create(f, outDir)).ToArray();

        IEnumerable<ConversionTarget> convert = targets;
        if (options.Filter.Any()) {
            convert = convert.Where(t => options.Filter.Contains(t.File.Name));
        }
        else {
            convert = convert.Where(t => !t.Exists || t.HasErrors || options.Update.Contains(t.File.Name) || options.Update.Contains("*"));
        }

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        var globalUsings = CompilationUnitConverter.GetGlobalStaticUsings(targets.Select(t => $"{project.Name}.{t.File.Name}")).NormalizeWhitespace();
        var globalUsingsPath = Path.Combine(outDir, "_VB6Usings.cs");
        File.WriteAllText(globalUsingsPath, globalUsings.ToFullString());

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

        var conversions = await Task.WhenAll(
            convert.Select(t => Task.Run(
                () => {
                    var file = t.File;
                    var stopwatch = Stopwatch.StartNew();
                    try {
                        var conversion = VB6ToCSharpConversion.ConvertFile(
                            file.Path, t.OutputPath, file.Name, project.Name, file.Type);

                        t.CompilationUnit = conversion.CompilationUnit;

                        if (options.Show) {
                            Console.WriteLine(t.CompilationUnit.ToFullString());
                        }

                        if (conversion.ParseErrors.Count > 0) {
                            var sb = new StringBuilder();
                            foreach (var error in conversion.ParseErrors) {
                                sb.AppendLine($"{BOLD}{file.Name}:{NOBOLD} PARSE ERROR: {error}");
                                sb.AppendLine(GetLineFromFile(conversion.Parse.Source, error.Line));
                            }

                            Console.Write($"{RED}{sb}{NORMAL}");
                            return conversion;
                        }

                        if (conversion.TransformErrors.Count > 0) {
                            var sb = new StringBuilder();
                            sb.AppendLine($"{file.Name} Converted with {conversion.TransformErrors.Count} errors.");
                            foreach (var err in conversion.TransformErrors) {
                                sb.AppendLine($"{file.Name}: {err.Message}");
                                sb.AppendLine();
                                sb.AppendLine(err.Source);
                                sb.AppendLine();
                                sb.AppendLine("=======================");
                                sb.AppendLine();
                            }

                            Console.Write($"{YELLOW}{sb}{NORMAL}");
                            return conversion;
                        }

                        if (conversion.SyntaxErrors.Count > 0) {
                            var sb = new StringBuilder();
                            sb.AppendLine($"{file.Name} Converted with {conversion.SyntaxErrors.Count} syntax errors.");
                            foreach (var diag in conversion.SyntaxErrors) {
                                sb.AppendLine($"{BOLD}{file.Name}:{NOBOLD} {diag}");
                            }

                            Console.Write(YELLOW + sb.ToString() + NORMAL);
                            return conversion;
                        }

                        Console.WriteLine($"{GREEN}{file.Name} Converted in {stopwatch.Elapsed}.{NORMAL}");
                        return conversion;
                    }
                    catch (Exception ex) when (!Debugger.IsAttached) {
                        Console.WriteLine($"{REVERSE}{RED}{file.Name} ERROR: {ex.Message}{NORMAL}");
                        return null;
                    }
                    finally {
                        stopwatch.Stop();
                    }
                })));

        if (targets.Any(t => !File.Exists(t.OutputPath))) {
            Console.WriteLine($"{RED}Some files have not yet been converted.{NORMAL}");
            return;
        }

        if (options.Filter is null) {
            Console.WriteLine("Compiling...");

            using (var workspace = MSBuildWorkspace.Create()) {
                var csproject = await workspace.OpenProjectAsync(projectPath);
                var compilation = await csproject.GetCompilationAsync();

                var diagnostics = compilation.GetDiagnostics();
                foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
                    Console.WriteLine($"{severity.Key}: {severity.Count()}");
                }

                using var writer = new StreamWriter(Path.Combine(outDir, "diagnostics.txt"), false);

                writer.WriteLine();
                writer.WriteLine("Global Diagnostics:");

                WriteDiagnostics(writer, diagnostics, "");

                writer.WriteLine();
                writer.WriteLine("=======================================================");
                writer.WriteLine("Files:");

                foreach (var file in diagnostics.GroupBy(d => d.Location.SourceTree.FilePath).OrderByDescending(f => f.Count())) {
                    writer.WriteLine($"{Path.GetFileNameWithoutExtension(file.Key)}:");
                    WriteDiagnostics(writer, file, "   ");
                }
            }
        }
    }

    static void WriteDiagnostics(StreamWriter writer, IEnumerable<Diagnostic> diagnostics, string prefix)
    {
        foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
            writer.WriteLine($"{prefix}{severity.Key}: {severity.Count()}");

            var ids = severity.GroupBy(gg => new { gg.Id, gg.Descriptor.MessageFormat })
                .Select(g => (id: g.Key, count: g.Count()))
                .OrderByDescending(g => g.count);

            foreach (var id in ids) {
                writer.WriteLine($"{prefix}{id.count,-6} - {id.id.Id} {id.id.MessageFormat}");
            }
        }

        writer.WriteLine();
    }

    static string GetLineFromFile(string code, int lineNumber)
    {
        using var reader = new StringReader(code);
        for (int i = 0; i < lineNumber - 1; i++) {
            reader.ReadLine();
        }
        return reader.ReadLine();
    }
}
