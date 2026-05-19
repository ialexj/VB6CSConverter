using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VB6Converter.ReferenceStubs;
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

        [Option("skip-reference-stubs", Required = false, HelpText = "Skips pre-semantic COM reference stub generation (enabled by default).")]
        public bool SkipReferenceStubs { get; set; }

        [Option("skip-transform", Required = false, HelpText = "Skips the transformation step and attempts to build with existing files.")]
        public bool SkipTransform { get; set; }

        [Option("skip-fixup", Required = false, HelpText = "Skips the fixup step.")]
        public bool SkipFixup { get; set; }

        [Option("skip-diagnostics", Required = false, HelpText = "Skips the diagnostics step.")]
        public bool SkipDiagnostics { get; set; }

        [Option("overwrite-user", Required = false, HelpText = "Overwrite files that don't have the GeneratedCode attribute.")]
        public bool OverwriteNonGenerated { get; set; }
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
            await GenerateReferenceStubs(vbProject, referenceDir);
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
            }
            else {
                AnsiConsole.MarkupLine("[yellow]Some files aren't yet fully converted.[/]");
            }
        }

        // At this point we should have the whole solution converted,
        // so we can build a semantic model and perform global rewrites.

        async Task<Compilation> GetCompilation()
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

        if (!options.SkipFixup) {
            AnsiConsole.MarkupLine("[yellow]Running fixups...[/]");

            bool hasChanges;
            int count = 0;
            do {
                hasChanges = false;
                Compilation compilation = null;

                async Task RunRewriter(bool compile, string title, Func<ConversionTarget, SemanticModel, LoggedRewriter> rewriter)
                {
                    if (compile && compilation is null || hasChanges) {
                        compilation = await GetCompilation();
                    }

                    hasChanges |= await RunOperations(title, ws.ActiveTargets,
                        (t, ctx, cancel) => ws.WithCompilationUnit(t, cancel, cu => {
                            var sm = compilation?.GetSemanticModel(cu.SyntaxTree, true);

                            var r = rewriter(t, sm);
                            r.Progress = (current, total) => {
                                ctx.IsIndeterminate = false;
                                ctx.MaxValue = total;
                                ctx.Value = current;
                            };

                            cu = (CompilationUnitSyntax)r.Visit(cu);
                            cu = (CompilationUnitSyntax)new UsingsRewriter(t.Name).Visit(cu);

                            return ValueTask.FromResult(cu);
                        }));
                }

                Log.Rewriting.Information("====== Starting Fixups ======");

                await RunRewriter(false, "Creating control singletons", (t, sem) => new ControlInstanceRewriter(ws.GetForms(), t.Name));
                await RunRewriter(false, "Fixing Foreach Variable", (t, sm) => new ForEachVariableRewriter());

                await RunRewriter(true, "Finding Types", (t, sm) => new TypeFinder(sm));
                await RunRewriter(true, "Finding Members", (t, sm) => new MemberFinder(sm));
                await RunRewriter(true, "Disambiguate Array Access", (t, sm) => new ArrayCallDisambiguator(sm));
                //await RunRewriter(true, "Rewriting parameterized property setters", (t, sm) => new ParameterizedPropertyRewriter(sm));


                var varTypes = new ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>();

                if (compilation is null || hasChanges) {
                    compilation = await GetCompilation();
                }

                await RunOperations("Collecting Variables", ws.Targets, 
                    (t, ctx, cancel) => ws.WithCompilationUnit(t, cancel, async cu => {
                        var sm = compilation.GetSemanticModel(cu.SyntaxTree, false);
                        await TypeRefiner.GetAllVariablesAndUsages(varTypes, sm, ws.Project.Solution);
                        return cu;
                    }));

                await RunRewriter(true, "Refining Types", (t, sm) => new TypeRefiner(varTypes));

                await RunRewriter(true, "Adding Type Casts", (t, sm) => new TypeCastRewriter(sm));
                //await RunRewriter(true, "Rewriting DAO", (t, sm) => new DAORewriter(sm));
                
                if (hasChanges) {
                    count++;
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Changes were made, re-running fixups ({count})...[/]");
                }
            }
            while (hasChanges);
        }

        // Collect diagnostics
        if (!options.SkipDiagnostics) {
            AnsiConsole.MarkupLine("[yellow]Collecting diagnostics...[/]");

            var compilation = await GetCompilation();
            AnsiConsole.Status()
                .Start("Collecting Diagnostics...", ctx => {
                    var diagnostics = compilation.GetDiagnostics();

                    foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
                        AnsiConsole.WriteLine($"{severity.Key}: {severity.Count()}");
                    }

                    using var writer = new StreamWriter(Path.Combine(options.OutputDir, "_Diagnostics.txt"), false);
                    DiagnosticsReport.Write(writer, diagnostics);
                });
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Reference stub generation
    // ─────────────────────────────────────────────────────────────────────

    static async Task GenerateReferenceStubs(
        VisualBasicProject vbProject,
        string outputDir)
    {
        AnsiConsole.MarkupLine("[yellow]Generating COM reference stubs...[/]");

        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[grey]Reference stub generation skipped (Windows only).[/]");
            return;
        }

        if (OperatingSystem.IsWindows()) {
            await GenerateReferenceStubsWindows(vbProject, outputDir);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static async Task GenerateReferenceStubsWindows(
        VisualBasicProject vbProject,
        string outputDir)
    {
        await AnsiConsole.Progress()
            .HideCompleted(true)
            .AutoClear(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new ElapsedTimeColumn()
            )
            .StartAsync(async ctx => {
                var overallTask = ctx.AddTask("Creating stubs for references...");

                Directory.CreateDirectory(outputDir);

                int resolved = 0, generated = 0, unresolved = 0;
                var reportLines = new ConcurrentBag<string>();
                var models = new ConcurrentBag<LibraryModel>();
                var seenGuids = new ConcurrentDictionary<Guid, Guid>();
                
                var inspectQueue = new ConcurrentQueue<VisualBasicProjectReference>();
                foreach (var reference in vbProject.References) {
                    inspectQueue.Enqueue(reference);
                }

                while (inspectQueue.Count > 0) {
                    var batch = new List<VisualBasicProjectReference>();
                    while (inspectQueue.TryDequeue(out var item)) {
                        batch.Add(item);
                    }

                    overallTask.MaxValue = batch.Count;

                    await Parallel.ForEachAsync(batch, async (reference, cancel) => {
                        var progress = ctx.AddTask(Path.GetFileName(reference.ResolvedPath));
                        progress.IsIndeterminate = true;

                        try {
                            if (!seenGuids.TryAdd(reference.Guid, reference.Guid)) {
                                return;
                            }

                            if (reference.ResolvedPath == null) {
                                Interlocked.Increment(ref unresolved);
                                Log.Default.Warning("Reference {description} ({guid}) could not be resolved", reference.Description, reference.Guid);
                                reportLines.Add($"UNRESOLVED  {reference.Description}  {{{reference.Guid}}}  v{reference.MajorVersion}.{reference.MinorVersion}");
                                return;
                            }

                            Interlocked.Increment(ref resolved);

                            var model = TypeLibraryInspector.Inspect(reference, reference.ResolvedPath);
                            if (model == null) {
                                reportLines.Add($"FAILED      {reference.Description}  {reference.ResolvedPath}");
                                return;
                            }

                            models.Add(model);

                            // Resolve transitive dependencies and enqueue for analysis in the next batch.
                            foreach (var dep in model.DiscoveredDependencies.Where(d => !seenGuids.ContainsKey(d.Guid))) {
                                var path = VisualBasicProject.ResolveTypeLibPath(dep.Guid, dep.Major, dep.Minor);
                                var depRef = new VisualBasicProjectReference(
                                    ProjectReferenceKind.TypeLibrary, dep.Guid, dep.Major, dep.Minor, 0,
                                    dep.Guid.ToString("B"), path, path);

                                inspectQueue.Enqueue(depRef);
                            }

                            // Generate the stubs
                            var written = ReferenceStubGenerator.Generate(model, outputDir);
                            Interlocked.Add(ref generated, written.Count);

                            reportLines.Add($"OK          {model.Name}  {reference.ResolvedPath}  ({written.Count} types)");
                            AnsiConsole.WriteLine($"  {model.Name}: {written.Count} stubs from {Path.GetFileName(reference.ResolvedPath)}");
                        }
                        catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached) {
                            Log.Default.Warning(ex, "Failed inspecting type library {description} ({guid})", reference.Description, reference.Guid);
                            reportLines.Add($"FAILED      {reference.Description}  {reference.ResolvedPath}");
                        }
                        finally {
                            progress.StopTask();
                            overallTask.Increment(1);
                            await Task.Yield();
                        }
                    });
                }

                // Collect aliases from all libraries (direct + transitive) and pass them as a
                // flat sequence so ReferenceUsingsGenerator can deduplicate by name.  Multiple COM
                // libraries (e.g. stdole and oleaut32) often define the same alias (OLE_COLOR, …)
                // and per-library global usings would cause CS0105 "appeared previously" errors.
                var allAliases = models.SelectMany(m => ReferenceStubGenerator.CollectAliases(m));
                var referenceUsingsPath = ReferenceUsingsGenerator.Generate(models, outputDir, allAliases);

                // Write summary report
                var reportPath = Path.Combine(outputDir, "_ReferenceStubs.txt");
                await File.WriteAllLinesAsync(reportPath, new[] {
                    $"Reference stubs generated: {generated}",
                    $"Libraries resolved:        {resolved}",
                    $"Libraries unresolved:      {unresolved}",
                    $"Reference usings file:     {referenceUsingsPath}",
                    string.Empty,
                }.Concat(reportLines));

                AnsiConsole.MarkupLineInterpolated(
                    $"[green]Reference stubs:[/] {generated} types from {resolved} libraries ({unresolved} unresolved). Report: {reportPath}");
            });
        
        
    }
}
