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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using VB6Converter.Rewriters;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using Microsoft.Build.Locator;
using System.Collections.Concurrent;
using System.Threading;


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

    [Option("skip-transform", Required = false, HelpText = "Skips the transformation step and attempts to build with existing files.")]
    public bool SkipConvert { get; set; }

    [Option("skip-fixup", Required = false, HelpText = "Skips the disambiguation step.")]
    public bool SkipFixup { get; set; }

    [Option("skip-diagnostics", Required = false, HelpText = "Skips the diagnostics step.")]
    public bool SkipDiagnostics { get; set; }
}

public static class Program
{
    public static Task Main(string[] args) => Run(Parser.Default.ParseArguments<CommandLineOptions>(args).Value);

    static async Task Run(CommandLineOptions options)
    {
        MSBuildLocator.RegisterDefaults();

        var project = VisualBasicProject.Load(options.Project);
        var outDir = options.Output;

        var targets = project.Files.Select(f => ConversionTarget.Create(f, outDir))
            .Where(t => !options.Filter.Any() || options.Filter.Contains(t.File.Name))
            .ToArray();

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        // Create project
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

            // Create a global usings file to replicate VB6's module accessibility
            var globalUsings = CompilationUnitConverter.GetGlobalStaticUsings(targets.Select(t => $"{project.Name}.{t.File.Name}")).NormalizeWhitespace();
            var globalUsingsPath = Path.Combine(outDir, "_VB6Usings.cs");
            File.WriteAllText(globalUsingsPath, globalUsings.ToFullString());
        }

        if (!options.SkipConvert) {
            IEnumerable<ConversionTarget> convert = targets;
            if (!options.Filter.Any()) {
                convert = convert.Where(t => !t.Exists || t.HasErrors || options.Update.Contains(t.File.Name) || options.Update.Contains("*"));
            }

            var conversions = Parallel.ForEach(convert, t => {
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
                    }

                    if (conversion.SyntaxErrors.Count > 0) {
                        var sb = new StringBuilder();
                        sb.AppendLine($"{file.Name} Converted with {conversion.SyntaxErrors.Count} syntax errors.");
                        foreach (var diag in conversion.SyntaxErrors) {
                            sb.AppendLine($"{BOLD}{file.Name}:{NOBOLD} {diag}");
                        }

                        Console.Write(YELLOW + sb.ToString() + NORMAL);
                    }

                    Console.WriteLine($"{GREEN}{file.Name} Converted in {stopwatch.Elapsed}.{NORMAL}");
                }
                catch (Exception ex) when (!Debugger.IsAttached) {
                    Console.WriteLine($"{REVERSE}{RED}{file.Name} ERROR: {ex.Message}{NORMAL}");
                }
                finally {
                    stopwatch.Stop();
                }
            });
        }

        if (targets.Any(t => !File.Exists(t.OutputPath))) {
            Console.WriteLine($"{RED}Some files have not yet been converted.{NORMAL}");
            return;
        }

        // Clear out missing types
        if (Directory.Exists(Path.Combine(outDir, "MissingTypes"))) {
            Directory.Delete(Path.Combine(outDir, "MissingTypes"), true);
        }

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (sender, e) => Log.Default.Warning("Workspace failed: {error}", e.Diagnostic);

        var csproject = await workspace.OpenProjectAsync(projectPath);

        if (!options.SkipFixup) {
            async Task RunFixup(Func<CompilationUnitSyntax, CancellationToken, ValueTask<CompilationUnitSyntax>> task)
            {
                // Fixing some issues will often unblock additional issues that can only be found on the next compile
                bool hasChanges = false;
                do {
                    Log.Default.Information("Compiling...");
                    var compilation = await csproject.GetCompilationAsync();

                    await Parallel.ForEachAsync(targets, async (t, c) => {
                        try {
                            var doc = csproject.Documents.FirstOrDefault(d => $"{d.Name}" == t.File.Name + ".cs") ?? throw new FileNotFoundException();
                            var st = await doc.GetSyntaxTreeAsync();
                            var cu = st.GetCompilationUnitRoot();

                            var newcu = await task(cu, c);

                            if (!newcu.IsEquivalentTo(cu)) {
                                await File.WriteAllTextAsync(doc.FilePath, newcu.ToFullString());

                                lock (workspace) {
                                    doc = doc.WithSyntaxRoot(newcu);
                                    csproject = doc.Project;
                                }

                                Log.Default.Debug("Wrote {file}.", Path.GetFileNameWithoutExtension(doc.FilePath));
                                Interlocked.Exchange(ref hasChanges, true);
                            }
                            else {
                                Interlocked.Exchange(ref hasChanges, false);
                            }
                        }
                        catch (Exception ex) {
                            Log.Default.ForContext("file", t.File.Name)
                                .Error(ex, "{file} failed to run fixup.");
                        }
                    });
                }
                while (hasChanges);
            }

            // Make controls use the instance singleton
            Log.Default.Information("Rewriting control singletons...");
            await RunFixup((cu, cancel) => {
                var controls = targets.Where(t => t.File.Type == VisualBasicFileType.Form).Select(t => t.File.Name);
                var formFixup = new ControlInstanceRewriter(controls);
                return ValueTask.FromResult((CompilationUnitSyntax)formFixup.Visit(cu));
            });

            // Use semantic information to fixup ambiguities
            Log.Default.Information("Applying semantic corrections...");
            await RunFixup(async (cu, cancel) => {
                var compilation = await csproject.GetCompilationAsync();
                var semantics = compilation.GetSemanticModel(cu.SyntaxTree, true);
                var disambiguator = new ArrayCallDisambiguator(semantics);
                return (CompilationUnitSyntax)disambiguator.Visit(cu);
            });
        }

        // Add missing types
        {
            Log.Default.Information("Collecting missing types...");
            
            var compilation = await csproject.GetCompilationAsync();

            var missingTypes = new MissingTypes();
            await Parallel.ForEachAsync(targets, async (target, cancel) => {
                var d  = csproject.Documents.FirstOrDefault(d => d.Name == target.File.Name + ".cs") ?? throw new FileNotFoundException();
                var t  = await d.GetSyntaxTreeAsync();
                var cu = t.GetCompilationUnitRoot();
                var sm = compilation.GetSemanticModel(t, true);
                new MissingTypeScanner(sm, missingTypes).Visit(cu);
            });

            foreach (var cu in missingTypes.GetCompilationUnits()) {
                var ns  = cu.DescendantNodes(i => i is not BaseNamespaceDeclarationSyntax).OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                var cls = cu.DescendantNodes(i => i is not ClassDeclarationSyntax).OfType<ClassDeclarationSyntax>().First();

                var path = Path.Combine(outDir, "MissingTypes",
                    $"{ns?.Name.ToString().Replace(".", new string(Path.PathSeparator, 1))}",
                    $"{cls.Identifier.Text}.cs");

                string fullName = ns != null ? $"{ns?.Name}.{cls.Identifier.Text}" : cls.Identifier.Text;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                await File.WriteAllTextAsync(path, cu.ToFullString());

                Log.Default.Debug("Generating {name} with {count} members.", fullName, cls.Members.Count);
            }
        }

        // Collect diagnostics
        if (!options.SkipDiagnostics) {
            Log.Default.Information("Collecting diagnostics...");
            Log.Default.Information("Compiling...");

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

            foreach (var file in diagnostics.Where(d => d.Location != null).GroupBy(d => d.Location.SourceTree.FilePath).OrderByDescending(f => f.Count())) {
                writer.WriteLine($"{Path.GetFileNameWithoutExtension(file.Key)}:");
                WriteDiagnostics(writer, file, "   ");
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
