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
        public string Root { get; set; }
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
        var workspaceRootOpt = new Option<string>("--root", ["-r"]) {
            Description = "Root directory of the VB6 workspace. Used to preserve folder structure for files referenced outside the project folder. Auto-detected from file paths when omitted.",
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
            workspaceRootOpt
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
                Root = result.GetValue(workspaceRootOpt)
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

        // Open/Create C# project
        using var ws = new ConversionWorkspace();
        var projectBasePath = Path.GetDirectoryName(Path.GetFullPath(options.Project)) ?? Directory.GetCurrentDirectory();

        var rootPath = options.Root is not null
            ? Path.GetFullPath(options.Root)
            : GetCommonAncestor(projectBasePath, vbProject.Files.Select(f => f.Path));

        AnsiConsole.MarkupLineInterpolated($"[grey]Workspace root: {rootPath}[/]");

        // Warn about files that fall outside the resolved root (only relevant when
        // --root is specified explicitly and is narrower than the actual file tree)
        foreach (var file in vbProject.Files) {
            var rel = Path.GetRelativePath(rootPath, file.Path);
            if (rel.Equals("..", StringComparison.Ordinal)
                || rel.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || rel.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)) {
                AnsiConsole.MarkupLineInterpolated(
                    $"[yellow]Warning: {file.Name} ({file.Path}) is outside the workspace root and will be placed at the output root.[/]");
            }
        }

        var allTargets = vbProject.Files.Select(f => ConversionTarget.Create(f, options.OutputDir, rootPath)).OrderBy(t => t.Name).ToArray();
        await ws.Open(allTargets, options.OutputDir, vbProject.Name);

        var conversionOptions = ConversionOptions.Default;

        ws.SetActiveFilter([.. options.Filter]);
        if (ws.ActiveTargets.Count == 0) {
            AnsiConsole.MarkupLine("[red]No files to convert.[/]");
            return;
        }

        // Do the code transformation
        var targetsThatNeedTransform = ws.ActiveTargets.Where(t => !t.Exists || t.HasErrors
            || options.Filter.Any()
            || options.Update.Contains(t.Name) || options.Update.Contains("*"))
            .ToArray();

        // Generate COM reference stubs
        if (!options.SkipReferenceStubs) {
            var referenceDir = Path.Join(options.OutputDir, "_References");
            await GenerateReferenceStubs(options.Project, referenceDir, options.ExcludeReferences);
            await CollectDiagnostics(ws, options.OutputDir);
            PauseIfRequested(options.Pause);
        }
        // ────────────────────────────────────────────────────────────────────

        if (!options.SkipTransform && targetsThatNeedTransform.Length > 0) {
            await RunOperations(ws, "Converting VB6 to C#", targetsThatNeedTransform, (t, ctx, cancel) =>
                ws.WithCompilationUnit(t, cancel, cu => {
                    var sourceRelativePath = Path.GetRelativePath(rootPath, t.File.Path).Replace('\\', '/');
                    var conversion = VB6ToCSharpConversion.ConvertFile(
                        t.File.Path, t.OutputPath, t.Name, vbProject.Name, t.File.Type, conversionOptions,
                        sourceRelativePath);

                    var st = SyntaxFactory.SyntaxTree(conversion.CompilationUnit, path: t.OutputPath);
                    return ValueTask.FromResult(st.GetCompilationUnitRoot(cancel));
                }));

            // Reload from disk before splitting: parallel saves during conversion leave
            // the in-memory Project stale (last-writer wins), so documents converted by
            // earlier threads are not found in the workspace and would be re-created empty.
            await ws.ReloadProject();

            // Split Form/Control designer code into separate *.designer.cs partial classes
            var formControlTargets = targetsThatNeedTransform
                .Where(t => t.File.Type is VisualBasicFileType.Form or VisualBasicFileType.Control)
                .ToArray();

            if (formControlTargets.Length > 0) {
                var newDesignerTargets = new ConcurrentBag<ConversionTarget>();
                await RunOperations(ws, "Splitting designer files", formControlTargets, (t, ctx, cancel) =>
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
        }

        // At this point we should have the whole solution converted,
        // so we can build a semantic model and perform global rewrites.
        if (!options.SkipFixup) {
            AnsiConsole.MarkupLine("[yellow]Running fixups...[/]");

            // Reset the rewriter sequence counter for this fixup phase
            RewriterSequenceContext.Reset();

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
                        if (compile && compilation is null) {
                            compilation = await CollectDiagnostics(ws, options.OutputDir);
                            PauseIfRequested(options.Pause);
                        }

                        hasRewriterChanges = await RunOperations(ws, title, ws.ActiveTargets,
                            async (t, ctx, cancel) => await ws.WithCompilationUnit(t, cancel, async cu => {
                                var sm = compilation?.GetSemanticModel(cu.SyntaxTree, true);

                                var r = await rewriter(t, sm);
                                // Assign the current sequence to this rewriter run
                                r.RewriterSequence = RewriterSequenceContext.GetNextSequence();
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
                            compilation = null;
                        }
                    }
                    while (hasRewriterChanges);
                }

                if (count == 0) {
                    // Structural rewrites, should work first time
                    await RunRewriter(true, "Expanding FRX-backed indexed designer assignments", async (t, sm) => new FrxExpansionRewriter(sm));
                    await RunRewriter(false, "Creating control singletons", async (t, sem) => new ControlInstanceRewriter(ws.GetForms(), t.Name));
                    await RunRewriter(false, "Fixing Foreach Variable", async(t, sm) => new ForEachVariableRewriter());
                    await RunRewriter(true, "Hoisting out-of-scope local declarations", async (t, sm) => new LocalDeclarationHoistingRewriter(sm));
                }

                await RunRewriter(true, "Finding Types", async (t, sm) => new TypeFinder(sm));
                await RunRewriter(true, "Qualifying Ambiguous Types", async (t, sm) => new AmbiguousTypeQualifier(sm, options.PreferredNamespaces));
                await RunRewriter(true, "Finding Members", async (t, sm) => new MemberFinder(sm));
                await RunRewriter(true, "Expanding default member usages", async (t, sm) => new DefaultMemberRewriter(sm));
                await RunRewriter(true, "Rewriting parameterless method-backed member access", async (t, sm) => new ParameterlessMethodCallRewriter(sm));

                await RunRewriter(true, "Rewriting default comparisons to null checks", async (t, sm) => new DefaultToNullRewriter(sm));
                await RunRewriter(true, "Rewriting bitwise Or/And", async (t, sm) => new BitwiseOrRewriter(sm));
                await RunRewriter(true, "Rewriting DateTime arithmetic", async (t, sm) => new DateTimeArithmeticRewriter(sm));
                await RunRewriter(true, "Disambiguate Array Access", async (t, sm) => new ArrayCallDisambiguator(sm));
                await RunRewriter(true, "Rewriting parameterized property setters", async (t, sm) => new ParameterizedPropertyRewriter(sm));

                await RunRewriter(true, "Refining Array Declarations", async (t, sm) => {
                    var declaratorTypes = new Dictionary<VariableDeclaratorSyntax, ArrayTypeSyntax>();
                    var symbolTypes = new Dictionary<ISymbol, ArrayTypeSyntax>(SymbolEqualityComparer.Default);
                    await ArrayRefinementRewriter.GetAllArrayVariablesAndUsages(sm, ws.Project.Solution, declaratorTypes, symbolTypes);
                    return new ArrayRefinementRewriter(sm, declaratorTypes, symbolTypes);
                });

                await RunRewriter(true, "Refining Types", async (t, sm) => {
                    var varTypes = new ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>();
                    await TypeRefiner.GetAllVariablesAndUsages(varTypes, sm, ws.Project.Solution);
                    return new TypeRefiner(varTypes);
                });

                await RunRewriter(true, "Coercing Literals", async (t, sm) => new LiteralCoercionRewriter(sm));
                await RunRewriter(true, "Casting Enums to Numbers", async (t, sm) => new EnumToNumberCastRewriter(sm));
                await RunRewriter(true, "Adding Type Casts", async (t, sm) => new TypeCastRewriter(sm));
                await RunRewriter(true, "Applying Type Conversions", async (t, sm) => new TypeConversionRewriter(sm));

                // Cosmetic - shouldn't change program meaning or reduce errors
                //await RunRewriter(true, "Collapsing local declaration + first assignment", async (t, sm) => new LocalDeclarationCollapseRewriter(sm));
                await RunRewriter(false, "Removing unneeded returns", async (t, sm) => new UnneededReturnRewriter());

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



    static async Task<Compilation> GetCompilation(ConversionWorkspace ws)
    {
        Compilation compilation = null;

        await AnsiConsole.Status()
            .StartAsync("Compiling...", async ctx => {
                var project = await ws.ReloadProject();
                compilation = await project.GetCompilationAsync();
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

    static void PauseIfRequested(bool pause)
    {
        if (!pause) return;
        AnsiConsole.MarkupLine("[grey]Press any key to continue (Ctrl-C to stop)...[/]");
        Console.ReadKey(intercept: true);
    }

    /// <summary>
    /// Returns the deepest common ancestor directory of all supplied paths plus
    /// <paramref name="projectBasePath"/>.  Falls back to <paramref name="projectBasePath"/>
    /// when paths span multiple drive roots (Windows) or share no common prefix.
    /// </summary>
    static string GetCommonAncestor(string projectBasePath, IEnumerable<string> filePaths)
    {
        var allDirs = filePaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? Path.GetFullPath(p))
            .Prepend(projectBasePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allDirs.Length == 1)
            return allDirs[0];

        var segments = allDirs
            .Select(d => d.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                                  StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var first = segments[0];
        var commonCount = first.Length;

        foreach (var seg in segments.Skip(1)) {
            var i = 0;
            while (i < commonCount && i < seg.Length &&
                   string.Equals(first[i], seg[i], StringComparison.OrdinalIgnoreCase))
                i++;
            commonCount = i;
        }

        if (commonCount == 0)
            return projectBasePath; // paths span different drives — fall back

        var ancestor = string.Join(Path.DirectorySeparatorChar, first.Take(commonCount));

        // Restore root separator for bare drive letters ("C:" → "C:\")
        if (ancestor.EndsWith(':'))
            ancestor += Path.DirectorySeparatorChar;

        return ancestor;
    }
}

