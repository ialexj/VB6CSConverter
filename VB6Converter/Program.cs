using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VB6Converter.Rewriters;
using VB6Parser;

namespace VB6Converter;

public class CommandLineOptions
{
    [Option('p', "project", Required = true, HelpText = "Path to the VB6 project file.")]
    public string Project { get; set; }

    [Option('o', "output", Required = true, HelpText = "Output directory for the converted files.")]
    public string OutputDir { get; set; }

    [Option('u', "update", Required = false, HelpText = "Files to update if already converted.")]
    public IEnumerable<string> Update { get; set; } = [];

    [Option('f', "filter", Required = false, HelpText = "Only process the specified files.")]
    public IEnumerable<string> Filter { get; set; } = [];

    [Option("show-output", Required = false, HelpText = "Print the converted file to the console.")]
    public bool Show { get; set; } = false;

    [Option("skip-transform", Required = false, HelpText = "Skips the transformation step and attempts to build with existing files.")]
    public bool SkipTransform { get; set; }

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
        ArgumentNullException.ThrowIfNull(options);

        var vbProject = VisualBasicProject.Load(options.Project);
        var outDir = options.OutputDir;

        var targets = vbProject.Files.Select(f => ConversionTarget.Create(f, options.OutputDir))
            .Where(t => !options.Filter.Any() || options.Filter.Contains(t.File.Name))
            .OrderBy(t => t.Name)
            .ToArray();

        using var ws = new ConversionWorkspace(targets);
        await ws.Open(options.OutputDir, vbProject.Name);

        // Transformation

        if (!options.SkipTransform) {
            await ws.RunOperations("Transforming code", (t, cu, log, cancel) => {
                if (t.Exists && !t.HasErrors && !options.Update.Contains(t.Name) && !options.Update.Contains("*")) {
                    return ValueTask.FromResult(cu);
                }

                var conversion = VB6ToCSharpConversion.ConvertFile(
                    t.File.Path, t.OutputPath, t.Name, vbProject.Name, t.File.Type);

                if (options.Show) {
                    Console.WriteLine(conversion.CompilationUnit.ToFullString());
                }

                return ValueTask.FromResult(conversion.CompilationUnit);
            });
        }
        else if (targets.Any(t => !t.Exists || t.HasErrors)) {
            Log.Default.Warning("Some files have not yet been fully converted.");
        }

        // At this point we should have the whole solution converted,
        // so we can build a semantic model and perform global rewrites.

        // Clear out missing types
        string missingTypesPath = Path.Combine(options.OutputDir, "MissingTypes");
        if (Directory.Exists(missingTypesPath)) {
            Directory.Delete(missingTypesPath, true);
        }

        if (!options.SkipFixup) {
            // Make controls use the instance singleton
            await ws.RunRewrites("Rewriting control singletons", (cu, compilation, log, cancel) => {
                var controls = targets.Where(t => t.File.Type == VisualBasicFileType.Form).Select(t => t.File.Name);
                var rewriter = new ControlInstanceRewriter(controls);
                return ValueTask.FromResult((CompilationUnitSyntax)rewriter.Visit(cu));
            });

            // Make controls use the instance singleton
            await ws.RunRewrites("Rewriting RecordSets", (cu, compilation, log, cancel) => {
                var controls  = targets.Where(t => t.File.Type == VisualBasicFileType.Form).Select(t => t.File.Name);
                var semantics = compilation.GetSemanticModel(cu.SyntaxTree, true);
                var rewriter  = new DAORewriter(semantics);
                return ValueTask.FromResult((CompilationUnitSyntax)rewriter.Visit(cu));
            });

            // Use semantic information to fixup ambiguities
            await ws.RunRewrites("Applying semantic corrections", (cu, compilation, log, cancel) => {
                var semantics = compilation.GetSemanticModel(cu.SyntaxTree, true);
                var disambiguator = new ArrayCallDisambiguator(semantics);
                return ValueTask.FromResult((CompilationUnitSyntax)disambiguator.Visit(cu));
            });
        }

        // Add missing types
        {
            var compilation = await ws.GetCompilation();
            var missingTypes = new MissingTypes();

            await ws.RunOperations("Collecting missing types", (t, cu, log, cancel) => {
                var sm = compilation.GetSemanticModel(cu.SyntaxTree, true);
                new MissingTypeScanner(sm, missingTypes).Visit(cu);
                return ValueTask.FromResult(cu);
            });

            foreach (var cu in missingTypes.GetCompilationUnits()) {
                var ns  = cu.DescendantNodes(i => i is not BaseNamespaceDeclarationSyntax).OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                var cls = cu.DescendantNodes(i => i is not ClassDeclarationSyntax).OfType<ClassDeclarationSyntax>().First();

                var path = Path.Combine(missingTypesPath,
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

            var compilation = await ws.GetCompilation();
            var diagnostics = compilation.GetDiagnostics();

            foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
                Console.WriteLine($"{severity.Key}: {severity.Count()}");
            }

            using var writer = new StreamWriter(Path.Combine(options.OutputDir, "_Diagnostics.txt"), false);
            DiagnosticsReport.Write(writer, diagnostics);
        }
    }
}
