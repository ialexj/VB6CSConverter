using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VB6Parser;

namespace VB6Converter;

public static class Program
{
    static async Task Main(string[] args)
    {
        //Trace.Listeners.Add(new TextWriterTraceListener("Input.log"));

        await ConvertProject(
            @"C:\Users\aj\source\repos\OptiwareVB6\Optiware\Optiware98.vbp",
            ["frmEntidadesMain"],
            //["modCommon", "modMain", "BaseDados"],
            "./Optiware98");
    }

    public static Task<Conversion[]> ConvertProject(string path, string[] filter, string outDir)
    {
        return ConvertProject(VisualBasicProject.Load(path), filter, outDir);
    }

    public static async Task<Conversion[]> ConvertProject(VisualBasicProject project, string[] filter, string outDir)
    {
        ArgumentNullException.ThrowIfNull(project);

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        var targets = project.Files.Select(f => new {
            f.Name, f.Path, f.Type,
            Target = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(f.Name)}.cs"),
            Convert = filter is null || filter.Contains(f.Name)
        }).ToArray();

        var conversions = await Task.WhenAll(
            targets.Select(file => Task.Run(
                () => {
                    if (file.Convert) {
                        try {
                            var conversion = VB6ToCSharpConverter.ConvertFile(file.Path, file.Name, project.Name, file.Type, file.Target);

                            if (conversion.Errors.Count > 0) {
                                var sb = new StringBuilder();
                                sb.AppendLine($"{file.Name} Converted with {conversion.Errors.Count} conversion errors.");
                                foreach (var diag in conversion.Errors) {
                                    sb.AppendLine($"{file.Name}: {diag.Message}");
                                }

                                Console.Write("\x1b[93m" + sb.ToString() + "\x1b[39m");
                            }
                            else {
                                var compilation = VB6ToCSharpConverter.GetCompilation([conversion.CompilationUnit]);
                                var diagnostics = compilation.GetParseDiagnostics();

                                if (diagnostics.Length > 0) {
                                    var sb = new StringBuilder();
                                    sb.AppendLine($"{file.Name} Converted with {diagnostics.Length} parse errors.");
                                    foreach (var diag in diagnostics) {
                                        sb.AppendLine($"{file.Name}: {diag}");
                                    }
                                    Console.Write("\x1b[93m" + sb.ToString() + "\x1b[39m");
                                }
                                else {
                                    Console.WriteLine($"{file.Name} Converted.");
                                }
                            }

                            return new Conversion(file.Name, conversion.CompilationUnit, null);
                        }
                        catch (Exception ex) when (!Debugger.IsAttached) {
                            Console.WriteLine($"{file.Name} ERROR: {ex.Message}");
                            return new Conversion(file.Name, null, ex);
                        }
                    }
                    else {
                        try {
                            var code = File.ReadAllText(file.Target);
                            var syntaxTree = CSharpSyntaxTree.ParseText(code);
                            return new Conversion(file.Name, syntaxTree.GetCompilationUnitRoot(), null);
                        }
                        catch (FileNotFoundException fnfe) {
                            Console.WriteLine($"{file.Name} has not been converted yet.");
                            return new Conversion(file.Name, null, fnfe);
                        }
                    }
                })));

        StringBuilder globalUsings = new();
        foreach (var file in targets) {
            globalUsings.AppendLine($"global using static {project.Name}.{file.Name};");
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

        return conversions;
    }

    public record struct Conversion(string Name, CompilationUnitSyntax CompilationUnit, Exception Error) { }
}
