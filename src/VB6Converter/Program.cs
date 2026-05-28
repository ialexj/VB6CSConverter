using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
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
        public string Project { get; set; } = null!;
        public string OutputDir { get; set; } = null!;
        public string[] Update { get; set; } = [];
        public string[] Filter { get; set; } = [];
        public bool Show { get; set; }
        public bool SkipReferenceStubs { get; set; }
        public bool SkipTransform { get; set; }
        public bool SkipFixup { get; set; }
        public bool SkipDiagnostics { get; set; }
        public string[] PreferredNamespaces { get; set; } = [];
        public string[] ExcludeReferences { get; set; } = [];
        public bool Pause { get; set; }
        public int SplitLines { get; set; } = 5000;
    }

    public static Task<int> Main(string[] args)
    {
        var projectOpt = new Option<string>("--project", ["-p"]) {
            Description = "Path to the VB6 project file.",
            Required = true,
        };
        var outputOpt = new Option<string>("--output", ["-o"]) {
            Description = "Output directory for the converted files.",
            Required = true,
        };
        var updateOpt = new Option<string[]>("--update", ["-u"]) {
            Description = "Files to update if already converted.",
        };
        var filterOpt = new Option<string[]>("--filter", ["-f"]) {
            Description = "Only process the specified files.",
        };
        var showOpt = new Option<bool>("--show-output", []) {
            Description = "Print the converted file to the console.",
        };
        var skipStubsOpt = new Option<bool>("--skip-stubs", []) {
            Description = "Skips COM reference stub generation.",
        };
        var skipTransformOpt = new Option<bool>("--skip-transform", []) {
            Description = "Skips the transformation step and attempts to build with existing files.",
        };
        var skipFixupOpt = new Option<bool>("--skip-fixup", []) {
            Description = "Skips the fixup step.",
        };
        var skipDiagnosticsOpt = new Option<bool>("--skip-diagnostics", []) {
            Description = "Skips the diagnostics step.",
        };
        var preferNamespacesOpt = new Option<string[]>("--prefer-namespace", ["-n"]) {
            Description = "Namespace prefixes to prefer when disambiguating ambiguous type references, in order of preference.",
        };
        var excludeRefsOpt = new Option<string[]>("--exclude-references", ["-xr"]) {
            Description = "COM library names to suppress stub generation for.",
        };
        var pauseOpt = new Option<bool>("--pause", []) {
            Description = "Pause for user input after each diagnostics collection. Press any key to continue, Ctrl-C to stop.",
        };
        var splitLinesOpt = new Option<int>("--split-lines", []) {
            Description = "Maximum lines per generated .cs file before splitting into numbered partial classes (0 = disabled). Designer files are never split.",
            DefaultValueFactory = _ => 5000,
        };

        var rootCommand = new RootCommand("Convert VB6 projects to C#.") {
            projectOpt,
            outputOpt,
            updateOpt,
            filterOpt,
            showOpt,
            skipStubsOpt,
            skipTransformOpt,
            skipFixupOpt,
            skipDiagnosticsOpt,
            preferNamespacesOpt,
            excludeRefsOpt,
            pauseOpt,
            splitLinesOpt
        };

        rootCommand.SetAction(async (ParseResult result) => {
            await Run(new CommandLineOptions {
                Project = result.GetValue(projectOpt)!,
                OutputDir = result.GetValue(outputOpt)!,
                Update = result.GetValue(updateOpt) ?? [],
                Filter = result.GetValue(filterOpt) ?? [],
                Show = result.GetValue(showOpt),
                SkipReferenceStubs = result.GetValue(skipStubsOpt),
                SkipTransform = result.GetValue(skipTransformOpt),
                SkipFixup = result.GetValue(skipFixupOpt),
                SkipDiagnostics = result.GetValue(skipDiagnosticsOpt),
                PreferredNamespaces = result.GetValue(preferNamespacesOpt) ?? [],
                ExcludeReferences = result.GetValue(excludeRefsOpt) ?? [],
                Pause = result.GetValue(pauseOpt),
                SplitLines = result.GetValue(splitLinesOpt),
            });
            return 0;
        });

        return rootCommand.Parse(args).InvokeAsync();
    }

    static async Task Run(CommandLineOptions options)
    {
        Directory.CreateDirectory(options.OutputDir);
        Log.Init(options.OutputDir);

        var vbProject = VisualBasicProject.Load(options.Project);

        // ── Pre-conversion: generate COM reference stubs ────────────────────
        if (!options.SkipReferenceStubs) {
            var referenceDir = Path.Join(options.OutputDir, "_References");
            await GenerateReferenceStubs(options.Project, referenceDir, options.ExcludeReferences);
        }
        // ────────────────────────────────────────────────────────────────────

        // Open/Create C# project
        using var ws = new ConversionWorkspace();
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
                var conversionOptions = ConversionOptions.Default;

                // ────────────────────────────────────────────────────────────────────

                await RunOperations("Converting VB6 to C#", targetsThatNeedTranform, (t, ctx, cancel) =>
                    ws.WithCompilationUnit(t, cancel, cu => {
                        var conversion = VB6ToCSharpConversion.ConvertFile(
                            t.File.Path, t.OutputPath, t.Name, vbProject.Name, t.File.Type, conversionOptions);

                        var st = SyntaxFactory.SyntaxTree(conversion.CompilationUnit, path: t.OutputPath);
                        return ValueTask.FromResult(st.GetCompilationUnitRoot(cancel));
                    }));

                // Split Form/Control designer code into separate *.designer.cs partial classes
                var formControlTargets = targetsThatNeedTranform
                    .Where(t => t.File.Type is VisualBasicFileType.Form or VisualBasicFileType.Control)
                    .ToArray();
                if (formControlTargets.Length > 0) {
                    var newDesignerTargets = new ConcurrentBag<ConversionTarget>();
                    await RunOperations("Splitting designer files", formControlTargets, (t, ctx, cancel) =>
                        ws.WithCompilationUnit(t, cancel, cu => {
                            var (mainCu, designerCu) = DesignerFileSplitter.Split(cu);
                            if (designerCu is not null) {
                                File.WriteAllText(t.DesignerOutputPath, designerCu.NormalizeWhitespace().ToFullString());
                                newDesignerTargets.Add(ConversionTarget.CreateForSplit(
                                    Path.GetFileNameWithoutExtension(t.DesignerOutputPath), t.DesignerOutputPath));
                            }
                            return ValueTask.FromResult(mainCu);
                        }));
                    ws.AddToActiveTargets(newDesignerTargets);
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
                await RunRewriter(true, "Casting Enums to Numbers", async (t, sm) => new EnumToNumberCastRewriter(sm));
                await RunRewriter(true, "Adding Type Casts", async (t, sm) => new TypeCastRewriter(sm));
                await RunRewriter(true, "Applying Type Conversions", async (t, sm) => new TypeConversionRewriter(sm));

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

        Log.CloseRewritingLoggers();
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
            foreach (var name in excludeList) {
                psi.ArgumentList.Add("--exclude-references");
                psi.ArgumentList.Add(name);
            }
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

