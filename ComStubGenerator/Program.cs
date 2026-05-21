using CommandLine;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VB6Parser;

namespace ComStubGenerator;

public static class Program
{
    public class CommandLineOptions
    {
        [Option('p', "project", Required = true, HelpText = "Path to the VB6 project file (.vbp).")]
        public string Project { get; set; } = null!;

        [Option('o', "output", Required = true, HelpText = "Output directory for generated stub files.")]
        public string OutputDir { get; set; } = null!;

        [Option("include-com-plumbing", Required = false, Default = false,
            HelpText = "Include COM infrastructure members (IUnknown, IDispatch, AddRef, Release, etc.) in generated stubs. Excluded by default.")]
        public bool IncludeComPlumbing { get; set; }

        [Option("force", Required = false, Default = false,
            HelpText = "Overwrite existing stub files and the global usings file. By default existing stubs are preserved so two runs (e.g. x86 then x64) can be combined.")]
        public bool Force { get; set; }
    }

    public static async Task<int> Main(string[] args)
    {
        var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
        if (parsed.Errors.Any()) {
            return 2;
        }

        return await Run(parsed.Value);
    }

    static async Task<int> Run(CommandLineOptions options)
    {
        Directory.CreateDirectory(options.OutputDir);
        Log.Init(options.OutputDir);

        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[grey]COM reference stub generation is only supported on Windows.[/]");
            return 2;
        }

        var vbProject = VisualBasicProject.Load(options.Project);

        if (vbProject.References.Count == 0) {
            AnsiConsole.MarkupLine("[grey]No COM references found in project.[/]");
            return 0;
        }

        bool anyFailed = await GenerateStubsWindows(vbProject, options.OutputDir, !options.IncludeComPlumbing, options.Force);
        return anyFailed ? 1 : 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static async Task<bool> GenerateStubsWindows(VisualBasicProject vbProject, string outputDir, bool filterComPlumbing, bool force)
    {
        bool anyFailed = false;

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
                    var batch = new System.Collections.Generic.List<VisualBasicProjectReference>();
                    while (inspectQueue.TryDequeue(out var item)) {
                        batch.Add(item);
                    }

                    overallTask.MaxValue = batch.Count;

                    await Parallel.ForEachAsync(batch, async (reference, cancel) => {
                        var progress = ctx.AddTask(Path.GetFileName(reference.ResolvedPath ?? reference.Description));
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

                            if (DotnetLibraryGuids.Contains(reference.Guid)) {
                                Log.Default.Information("Skipping reference {description} ({guid})", reference.Description, reference.Guid);
                                reportLines.Add($"SKIPPED     {reference.Description}  {reference.ResolvedPath}");
                                return;
                            }

                            Interlocked.Increment(ref resolved);

                            var model = TypeLibraryInspector.Inspect(reference, reference.ResolvedPath);
                            if (model == null) {
                                anyFailed = true;
                                reportLines.Add($"FAILED      {reference.Description}  {reference.ResolvedPath}");
                                return;
                            }

                            models.Add(model);

                            // Resolve transitive dependencies and enqueue for analysis in the next batch.
                            foreach (var dep in model.DiscoveredDependencies.Where(d => !seenGuids.ContainsKey(d.Guid))) {
                                var path = VisualBasicProject.ResolveTypeLibPath(dep.Guid, dep.Major, dep.Minor);
                                if (path != null) {
                                    var depRef = new VisualBasicProjectReference(
                                        ProjectReferenceKind.TypeLibrary, dep.Guid, dep.Major, dep.Minor, 0,
                                        dep.Guid.ToString("B"), path, path, true);

                                    inspectQueue.Enqueue(depRef);
                                }
                            }

                            // Generate the stubs
                            var written = ReferenceStubGenerator.Generate(model, outputDir, filterComPlumbing, force);
                            Interlocked.Add(ref generated, written.Count);

                            reportLines.Add($"OK          {model.Name} - {model.Guid} - {reference.ResolvedPath}  ({written.Count} types)");
                            AnsiConsole.WriteLine($"  {model.Name}: {written.Count} stubs from {Path.GetFileName(reference.ResolvedPath)}");
                        }
                        catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached) {
                            anyFailed = true;
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

                // Only direct (non-transitive) libraries contribute to the global usings file.
                // Transitive stubs are still generated so types resolve during compilation, but
                // their namespaces and enum statics are not surfaced as global usings.
                var directModels = models.Where(m => !m.IsTransitive).ToList();
                var allAliases = directModels.SelectMany(m => ReferenceStubGenerator.CollectAliases(m));
                var referenceUsingsPath = ReferenceUsingsGenerator.Generate(directModels, outputDir, allAliases, force);

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

        return anyFailed;
    }
}
