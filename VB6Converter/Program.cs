using CommandLine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    static Task Main(string[] args) => Main(Parser.Default.ParseArguments<CommandLineOptions>(args).Value);

    public static async Task Main(CommandLineOptions options)
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

        var conversions = await Task.WhenAll(
            convert.Select(t => Task.Run(
                () => {
                    var file = t.File;
                    var stopwatch = Stopwatch.StartNew();
                    try {
                        var conversion = VB6ToCSharpConverter.ConvertFile(
                            file.Path, t.OutputPath, file.Name, project.Name, file.Type);

                        t.CompilationUnit = conversion.CompilationUnit;

                        if (options.Show) {
                            Console.WriteLine(t.CompilationUnit.ToFullString());
                        }

                        if (conversion.Errors.Count > 0) {
                            var sb = new StringBuilder();
                            sb.AppendLine($"{file.Name} Converted with {conversion.Errors.Count} conversion errors.");
                            foreach (var err in conversion.Errors) {
                                sb.AppendLine($"{file.Name}: {err.Message}");
                            }

                            File.WriteAllText(file.Path + ".log", sb.ToString());
                            Console.Write($"{YELLOW}{sb}{NORMAL}");
                            return conversion;
                        }
                        else {
                            File.Delete(file.Path + ".log");
                        }

                        var compilation = VB6ToCSharpConverter.GetCompilation([conversion.CompilationUnit]);
                        var diagnostics = compilation.GetParseDiagnostics();

                        if (diagnostics.Length > 0) {
                            var sb = new StringBuilder();
                            sb.AppendLine($"{file.Name} Converted with {diagnostics.Length} syntax errors.");
                            foreach (var diag in diagnostics) {
                                sb.AppendLine($"{BOLD}{file.Name}:{NOBOLD} {diag}");
                            }

                            Console.Write(YELLOW + sb.ToString() + NORMAL);
                            return conversion;
                        }

                        stopwatch.Stop();
                        Console.WriteLine($"{GREEN}{file.Name} Converted in {stopwatch.Elapsed}.{NORMAL}");
                        return conversion;
                    }
                    catch (ParseException parse) {
                        var sb = new StringBuilder();
                        foreach (var error in parse.Errors) {
                            sb.AppendLine($"{BOLD}{file.Name}:{NOBOLD} PARSE ERROR: {error}");
                            sb.AppendLine(GetLineFromFile(file.Path, error.Line));
                        }

                        Console.Write($"{RED}{sb}{NORMAL}");
                        return null;
                    }
                    catch (Exception ex) when (!Debugger.IsAttached) {
                        Console.WriteLine($"{REVERSE}{RED}{file.Name} ERROR: {ex.Message}{NORMAL}");
                        return null;
                    }
                    finally {
                        stopwatch.Stop();
                    }
                })));

        StringBuilder globalUsings = new();
        foreach (var target in targets) {
            globalUsings.AppendLine($"global using static {project.Name}.{target.File.Name};");
        }

        var globalUsingsPath = Path.Combine(outDir, "_VB6Usings.cs");
        File.WriteAllText(globalUsingsPath, globalUsings.ToString());

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

    }

    static string GetLineFromFile(string file, int lineNumber)
    {
        using var reader = new StreamReader(file);
        for (int i = 0; i < lineNumber - 1; i++) {
            reader.ReadLine();
        }
        return reader.ReadLine();
    }
}
