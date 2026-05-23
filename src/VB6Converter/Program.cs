using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VB6Converter.Rewriters;
using VB6Converter.Rewriters.Semantic;
using VB6Parser;
using static VB6Converter.ConsoleHelpers;

namespace VB6Converter;

public static class Program
{
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

        [Option("skip-stubs", Required = false, HelpText = "Skips pre-semantic COM reference stub generation (enabled by default).")]
        public bool SkipReferenceStubs { get; set; }

        [Option("skip-transform", Required = false, HelpText = "Skips the transformation step and attempts to build with existing files.")]
        public bool SkipTransform { get; set; }

        [Option("skip-fixup", Required = false, HelpText = "Skips the fixup step.")]
        public bool SkipFixup { get; set; }

        [Option("skip-diagnostics", Required = false, HelpText = "Skips the diagnostics step.")]
        public bool SkipDiagnostics { get; set; }

        [Option("overwrite-user", Required = false, HelpText = "Overwrite files that don't have the GeneratedCode attribute.")]
        public bool OverwriteNonGenerated { get; set; }

        [Option('n', "prefer-namespace", Required = false, HelpText = "Namespace prefixes to prefer when disambiguating ambiguous type references, in order of preference.")]
        public IEnumerable<string> PreferredNamespaces { get; set; } = [];

        [Option("exclude-references", Required = false, HelpText = "COM library names to suppress stub generation for.")]
        public IEnumerable<string> ExcludeReferences { get; set; } = [];

        [Option("pause", Required = false, HelpText = "Pause for user input after each diagnostics collection. Press any key to continue, Ctrl-C to stop.")]
        public bool Pause { get; set; }

        [Option("split-lines", Default = 5000, HelpText = "Maximum lines per generated .cs file before splitting into numbered partial classes (0 = disabled). Designer files are never split.")]
        public int SplitLines { get; set; } = 5000;
    }

    public static Task Main(string[] args)
    {
        var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
        if (parsed.Errors.Any()) {
            return Task.CompletedTask;
        }

        return Run(parsed.Value);
    }

    static async Task Run(CommandLineOptions options)
    {
        Directory.CreateDirectory(options.OutputDir);
        Log.Init(options.OutputDir);

        var vbProject = VisualBasicProject.Load(options.Project);

        // ── Pre-conversion: generate COM reference stubs ────────────────────
        if (!options.SkipReferenceStubs && vbProject.References.Count > 0) {
            var referenceDir = Path.Join(options.OutputDir, "_References");
            await GenerateReferenceStubs(options.Project, referenceDir, options.ExcludeReferences);
        }
        // ────────────────────────────────────────────────────────────────────

        // Open/Create C# project
        using var ws = new ConversionWorkspace(options.OverwriteNonGenerated);
        var projectBasePath = Path.GetDirectoryName(Path.GetFullPath(options.Project)) ?? Directory.GetCurrentDirectory();
        var allTargets = vbProject.Files.Select(f => ConversionTarget.Create(f, options.OutputDir, projectBasePath)).OrderBy(t => t.Name).ToArray();
        await ws.Open(allTargets, options.OutputDir, vbProject.Name);
        ws.SetActiveFilter([.. options.Filter]);

        if (ws.ActiveTargets.Count == 0) {
            AnsiConsole.MarkupLine("[red]No files to convert.[/]");
            return;
        }

        // Do the code transformation
        var targetsThatNeedTranform = ws.ActiveTargets.Where(t => !t.Exists || t.HasErrors
            || options.Filter.Any()
            || options.Update.Contains(t.Name) || options.Update.Contains("*"))
            .ToArray();

        if (targetsThatNeedTranform.Length > 0) {
            if (!options.SkipTransform) {
                await RunOperations("Converting VB6 to C#", targetsThatNeedTranform, (t, ctx, cancel) =>
                    ws.WithCompilationUnit(t, cancel, cu => {
                        var conversion = VB6ToCSharpConversion.ConvertFile(
                            t.File.Path, t.OutputPath, t.Name, vbProject.Name, t.File.Type);

                        var st = SyntaxFactory.SyntaxTree(conversion.CompilationUnit, path: t.OutputPath);
                        return ValueTask.FromResult(st.GetCompilationUnitRoot(cancel));
                    }));

                // Split Form/Control designer code into separate *.designer.cs partial classes
                var formControlTargets = targetsThatNeedTranform
                    .Where(t => t.File.Type is VisualBasicFileType.Form or VisualBasicFileType.Control)
                    .ToArray();
                if (formControlTargets.Length > 0) {
                    await RunOperations("Splitting designer files", formControlTargets, (t, ctx, cancel) =>
                        ws.WithCompilationUnit(t, cancel, cu => {
                            var (mainCu, designerCu) = DesignerFileSplitter.Split(cu);
                            if (designerCu is not null)
                                File.WriteAllText(t.DesignerOutputPath, designerCu.NormalizeWhitespace().ToFullString());
                            return ValueTask.FromResult(mainCu);
                        }));
                }

                // Split files that exceed the line budget into numbered partial classes
                if (options.SplitLines > 0) {
                    foreach (var t in targetsThatNeedTranform) {
                        if (!File.Exists(t.OutputPath)) continue;

                        var content = await File.ReadAllTextAsync(t.OutputPath);
                        var cu = CSharpSyntaxTree.ParseText(content, path: t.OutputPath).GetCompilationUnitRoot();
                        var chunks = LargeFileSplitter.Split(cu, options.SplitLines);
                        if (chunks.Count <= 1) continue;

                        var baseName = Path.GetFileNameWithoutExtension(t.OutputPath);
                        var dir = Path.GetDirectoryName(t.OutputPath)!;

                        // Remove numbered files left over from a previous run
                        foreach (var f in Directory.GetFiles(dir, baseName + "*.cs")) {
                            var stem = Path.GetFileNameWithoutExtension(f);
                            if (stem.Length > baseName.Length && stem[baseName.Length..].All(char.IsDigit))
                                File.Delete(f);
                        }

                        // Write each chunk and register it as a new conversion target
                        var splitTargets = new List<ConversionTarget>(chunks.Count);
                        for (int i = 0; i < chunks.Count; i++) {
                            var name = baseName + (i + 1);
                            var path = Path.Combine(dir, name + ".cs");
                            await File.WriteAllTextAsync(path, chunks[i].NormalizeWhitespace().ToFullString());
                            splitTargets.Add(ConversionTarget.CreateForSplit(name, path));
                        }

                        File.Delete(t.OutputPath);
                        ws.ReplaceWithSplitParts(t, splitTargets);
                    }
                }
            }
            else {
                AnsiConsole.MarkupLine("[yellow]Some files aren't yet fully converted.[/]");
            }
        }

        // At this point we should have the whole solution converted,
        // so we can build a semantic model and perform global rewrites.
        if (!options.SkipFixup) {
            AnsiConsole.MarkupLine("[yellow]Running fixups...[/]");

            bool hasChanges;
            int count = 0;
            do {
                // Reload from disk to avoid stale in-memory project state caused by
                // parallel SaveDocument writes during the conversion phase (each
                // thread's doc.Project snapshot only carries its own file's update,
                // so the last writer wins and all other documents appear empty).
                await ws.ReloadProject();

                hasChanges = false;
                Compilation compilation = null;

                async Task RunRewriter(bool compile, string title, Func<ConversionTarget, SemanticModel, Task<LoggedRewriter>> rewriter)
                {
                    bool hasRewriterChanges = false;
                    do {
                        if (compile && compilation is null || hasRewriterChanges || hasChanges) {
                            compilation = await CollectDiagnostics(ws, options.OutputDir);
                            PauseIfRequested(options.Pause);
                        }

                        hasRewriterChanges = await RunOperations(title, ws.ActiveTargets,
                            async (t, ctx, cancel) => await ws.WithCompilationUnit(t, cancel, async cu => {
                                var sm = compilation?.GetSemanticModel(cu.SyntaxTree, true);

                                var r = await rewriter(t, sm);
                                r.Progress = (current, total) => {
                                    ctx.IsIndeterminate = false;
                                    ctx.MaxValue = total;
                                    ctx.Value = current;
                                };

                                cu = (CompilationUnitSyntax)r.Visit(cu);
                                cu = (CompilationUnitSyntax)new UsingsRewriter(t.Name).Visit(cu);
                                return cu;
                            }));

                        if (hasRewriterChanges) {
                            hasChanges = true;
                        }
                    }
                    while (hasRewriterChanges);
                }

                Log.Rewriting.Information("====== Starting Fixups ======");

                if (count == 0) {
                    // These rewrites work first time
                    await RunRewriter(false, "Creating control singletons", async (t, sem) => new ControlInstanceRewriter(ws.GetForms(), t.Name));
                    await RunRewriter(false, "Fixing Foreach Variable", async(t, sm) => new ForEachVariableRewriter());
                }

                await RunRewriter(true, "Finding Types", async (t, sm) => new TypeFinder(sm));
                await RunRewriter(true, "Qualifying Ambiguous Types", async (t, sm) => new AmbiguousTypeQualifier(sm, options.PreferredNamespaces));
                await RunRewriter(true, "Finding Members", async (t, sm) => new MemberFinder(sm));

                await RunRewriter(true, "Rewriting bitwise Or/And", async (t, sm) => new BitwiseOrRewriter(sm));
                await RunRewriter(true, "Disambiguate Array Access", async (t, sm) => new ArrayCallDisambiguator(sm));
                await RunRewriter(true, "Rewriting parameterized property setters", async (t, sm) => new ParameterizedPropertyRewriter(sm));

                await RunRewriter(true, "Refining Types", async (t, sm) => {
                    var varTypes = new ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>();
                    await TypeRefiner.GetAllVariablesAndUsages(varTypes, sm, ws.Project.Solution);
                    return new TypeRefiner(varTypes);
                });

                await RunRewriter(true, "Coercing Literals", async (t, sm) => new LiteralCoercionRewriter(sm));
                await RunRewriter(true, "Adding Type Casts", async (t, sm) => new TypeCastRewriter(sm));

                //await RunRewriter(true, "Rewriting DAO", async (t, sm) => new DAORewriter(sm));

                if (hasChanges) {
                    count++;
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Changes were made, re-running fixups ({count})...[/]");
                }
            }
            while (hasChanges);
        }

        // Collect diagnostics
        if (!options.SkipDiagnostics) {
            await CollectDiagnostics(ws, options.OutputDir);
            PauseIfRequested(options.Pause);
        }
    }

    static void PauseIfRequested(bool pause)
    {
        if (!pause) return;
        AnsiConsole.MarkupLine("[grey]Press any key to continue (Ctrl-C to stop)...[/]");
        Console.ReadKey(intercept: true);
    }

    static async Task<Compilation> GetCompilation(ConversionWorkspace ws)
    {
        Compilation compilation = null;

        await AnsiConsole.Status()
            .StartAsync("Compiling...", async ctx => {
                var project = await ws.ReloadProject();
                compilation = await project.GetCompilationAsync();

                Log.Rewriting.Information("===== Compilation Statistics =====");
                var diagnostics = compilation.GetDiagnostics();
                foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
                    Log.Rewriting.Information($"{severity.Key}: {severity.Count()}");
                }
            });

        return compilation;
    }

    static async Task<Compilation> CollectDiagnostics(ConversionWorkspace ws, string outputDir)
    {
        AnsiConsole.MarkupLine("[yellow]Collecting diagnostics...[/]");

        var compilation = await GetCompilation(ws);
        AnsiConsole.Status()
            .Start("Collecting Diagnostics...", ctx => {
                var diagnostics = compilation.GetDiagnostics();

                using var writer = new StreamWriter(Path.Combine(outputDir, "_Diagnostics.txt"), false);
                DiagnosticsReport.Write(writer, diagnostics);

                var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                if (errorCount > 0) {
                    AnsiConsole.MarkupLineInterpolated($"[red]Errors: {errorCount}[/]");
                }
            });

        return compilation;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Reference stub generation — delegates to the ComStubGenerator executable
    // ─────────────────────────────────────────────────────────────────────

    static async Task GenerateReferenceStubs(string projectPath, string outputDir, IEnumerable<string> excludeReferences)
    {
        AnsiConsole.MarkupLine("[yellow]Generating COM reference stubs...[/]");

        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[grey]Reference stub generation skipped (Windows only).[/]");
            return;
        }

        var stubGenExe = FindComStubGeneratorExe();
        if (!File.Exists(stubGenExe)) {
            AnsiConsole.MarkupLineInterpolated($"[red]ComStubGenerator.exe not found at {stubGenExe}. Skipping reference stubs.[/]");
            Log.Default.Warning("ComStubGenerator.exe not found; reference stubs will not be generated");
            return;
        }

        var psi = new ProcessStartInfo {
            FileName = stubGenExe,
            ArgumentList = { "-p", projectPath, "-o", outputDir },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var excludeList = excludeReferences.ToList();
        if (excludeList.Count > 0) {
            psi.ArgumentList.Add("--exclude-references");
            foreach (var name in excludeList)
                psi.ArgumentList.Add(name);
        }

        using var process = Process.Start(psi)!;

        var stdoutTask = RelayOutputAsync(process.StandardOutput);
        var stderrTask = RelayOutputAsync(process.StandardError);

        await process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask);

        if (process.ExitCode != 0) {
            AnsiConsole.MarkupLineInterpolated($"[yellow]ComStubGenerator exited with code {process.ExitCode}; some reference stubs may be missing.[/]");
        }

        static async Task RelayOutputAsync(System.IO.TextReader reader)
        {
            string line;
            while ((line = await reader.ReadLineAsync()) != null) {
                AnsiConsole.WriteLine(line);
            }
        }
    }

    static string FindComStubGeneratorExe()
    {
        string baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "stubs", "ComStubGenerator.exe");
    }
}

