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
using VB6Converter.Rewriters.Forms;
using VB6Converter.Rewriters.Semantic;
using VB6Parser;
using static VB6Converter.ConsoleUI;

namespace VB6Converter;

public static class Program
{
    public class ConverterOptions
    {
        public string Project { get; set; } = null!;
        public string OutputDir { get; set; } = null!;
        public string Root { get; set; }
        public string[] Filter { get; set; } = [];
        public bool Show { get; set; }
        public bool SkipCleanSource { get; set; }
        public bool SkipReferenceStubs { get; set; }
        public bool SkipTransform { get; set; }
        public bool SkipRefinement { get; set; }
        public bool SkipDiagnostics { get; set; }
        public string[] PreferredNamespaces { get; set; } = [];
        public string[] ExcludeReferences { get; set; } = [];
        public bool Pause { get; set; }
        public bool Verbose { get; set; }
        public bool Git { get; set; }
        public string DiagnosticsPath { get; set; }

    }

    public static Task<int> Main(string[] args)
    {
        var rootCommand = GetRootCommand();
        return rootCommand.Parse(args).InvokeAsync();
    }

    static RootCommand GetRootCommand()
    {
        var projectOpt = new Option<string>("--project", ["-p"]) {
            Description = "Path to the VB6 project file.",
            Required = true,
        };
        var outputOpt = new Option<string>("--output", ["-o"]) {
            Description = "Output directory for the converted files.",
            Required = true,
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
        var verboseOpt = new Option<bool>("--verbose", ["-v"]) {
            Description = "Enable verbose logging.",
        };
        var gitOpt = new Option<bool>("--git", []) {
            Description = "Commit to git after each rewriter pass. The output directory must already be a git repository.",
        };
        var skipCleanOpt = new Option<bool>("--skip-clean-source", []) {
            Description = "Does not persist the preprocessed source back to the input file. Doing so may break source backreferences.",
        };
        var diagnosticsPathOpt = new Option<string>("--diagnostics-path", []) {
            Description = "Path for the diagnostics report file. Defaults to _Diagnostics.txt in the output directory.",
        };

        var rootCommand = new RootCommand("Convert VB6 projects to C#.")
        {
            projectOpt,
            outputOpt,
            filterOpt,
            showOpt,
            skipStubsOpt,
            skipTransformOpt,
            skipFixupOpt,
            skipDiagnosticsOpt,
            preferNamespacesOpt,
            excludeRefsOpt,
            pauseOpt,
            workspaceRootOpt,
            verboseOpt,
            gitOpt,
            skipCleanOpt,
            diagnosticsPathOpt,
            GetDiagnosticsCommand()
        };

        rootCommand.SetAction(async (ParseResult result) => {
            await RunConverter(new ConverterOptions {
                Project = result.GetValue(projectOpt)!,
                OutputDir = result.GetValue(outputOpt)!,
                Filter = result.GetValue(filterOpt) ?? [],
                Show = result.GetValue(showOpt),
                SkipReferenceStubs = result.GetValue(skipStubsOpt),
                SkipCleanSource = result.GetValue(skipCleanOpt),
                SkipTransform = result.GetValue(skipTransformOpt),
                SkipRefinement = result.GetValue(skipFixupOpt),
                SkipDiagnostics = result.GetValue(skipDiagnosticsOpt),
                PreferredNamespaces = result.GetValue(preferNamespacesOpt) ?? [],
                ExcludeReferences = result.GetValue(excludeRefsOpt) ?? [],
                Pause = result.GetValue(pauseOpt),
                Root = result.GetValue(workspaceRootOpt),
                Verbose = result.GetValue(verboseOpt),
                Git = result.GetValue(gitOpt),
                DiagnosticsPath = result.GetValue(diagnosticsPathOpt),
            });
            return 0;
        });
        return rootCommand;
    }

    static Command GetDiagnosticsCommand()
    {
        var command = new Command("diagnostics", "Collects machine-readable diagnostics for the converted project.")
        {
            new Option<string>("--project", ["-p"]) {
                Description = "Path to the converted C# project file (.csproj).",
                Required = true,
            },
            new Option<string>("--output-json", ["-oj"]) {
                Description = "Exact path for the JSON diagnostics report file. If omitted, JSON output is skipped.",
            },
            new Option<string>("--output-text", ["-ot"]) {
                Description = "Exact path for the text diagnostics report file. If omitted, text output is skipped.",
            },
        };

        command.SetAction(async (ParseResult parse) => await RunDiagnostics(
            parse.GetValue<string>("--project"),
            parse.GetValue<string>("--output-json"),
            parse.GetValue<string>("--output-text"))
        );

        return command;
    }

    static async Task RunDiagnostics(string projectPath, string? jsonPath, string? txtPath)
    {
        using var ws = new ConversionWorkspace();
        await ws.Open(projectPath);
        await ws.Compile();

        var diagnostics = ws.Compilation.GetDiagnostics();
        var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

        if (jsonPath is null && txtPath is null) {
            AnsiConsole.MarkupLineInterpolated($"[red]Errors: {errorCount}[/]");
            return;
        }

        // Determine the output root for path normalization: use the directory
        // of whichever output file is provided (prefer JSON's directory).
        var outputRoot = jsonPath is not null
            ? Path.GetDirectoryName(Path.GetFullPath(jsonPath))!
            : Path.GetDirectoryName(Path.GetFullPath(txtPath!))!;

        if (jsonPath is not null) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
            using var jsonWriter = new StreamWriter(jsonPath, false);
            DiagnosticsReport.WriteJson(jsonWriter, diagnostics, outputRoot);
            AnsiConsole.MarkupLineInterpolated($"[green]JSON report: {jsonPath}[/]");
        }

        if (txtPath is not null) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(txtPath))!);
            using var txtWriter = new StreamWriter(txtPath, false);
            DiagnosticsReport.Write(txtWriter, diagnostics, outputRoot);
            AnsiConsole.MarkupLineInterpolated($"[green]Text report: {txtPath}[/]");
        }

        AnsiConsole.MarkupLineInterpolated($"[red]Errors: {errorCount}[/]");
        AnsiConsole.MarkupLineInterpolated($"[green]JSON report: {jsonPath}[/]");
        AnsiConsole.MarkupLineInterpolated($"[green]Text report: {txtPath}[/]");
    }

    static async Task RunConverter(ConverterOptions options)
    {
        // Set up logging
        Directory.CreateDirectory(options.OutputDir);
        Log.Init(options.OutputDir, options.Verbose);

        // Validate git
        if (options.Git) {
            await GitUtils.EnsureGitAvailable(options.OutputDir);
        }

        // Load the VB6 project
        var vbProject = VisualBasicProject.Load(options.Project);

        // Determine root path for the workspace
        var projectBasePath = Path.GetDirectoryName(Path.GetFullPath(options.Project)) ?? Directory.GetCurrentDirectory();
        var rootPath = options.Root is not null
            ? Path.GetFullPath(options.Root)
            : PathHelper.GetCommonAncestor(projectBasePath, vbProject.Files.Select(f => f.Path));
        AnsiConsole.MarkupLineInterpolated($"[grey]Workspace root: {rootPath}[/]");

        // Set up preferred namespaces
        var preferredNamespaces = new PreferredNamespaceList(options.PreferredNamespaces);

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

        // Open/Create C# project
        using var ws = new ConversionWorkspace();
        var allTargets = vbProject.Files.SelectMany(f => ConversionTarget.CreateAll(f, options.OutputDir, rootPath)).OrderBy(t => t.Name).ToArray();
        await ws.Open(allTargets, options.OutputDir, vbProject.Name);

        ws.SetActiveFilter(options.Filter);
        if (ws.ActiveTargets.Count == 0) {
            AnsiConsole.MarkupLine("[red]No files to convert.[/]");
            return;
        }

        // Generate COM reference stubs
        if (!options.SkipReferenceStubs) {
            await RunPipeline(new ComStubPipeline(ws, options));
        }
        // ────────────────────────────────────────────────────────────────────

        if (!options.SkipCleanSource) {
            await RunPipeline(new CleanSourcePipeline(ws, options));
        }

        if (!options.SkipTransform) {
            // Do the code transformation
            await RunPipeline(new ConversionPipeline(ws, options));

            // Split designer code into separate *.designer.cs files for Forms and Controls
            await RunPipeline(new SplitDesignerPipeline(ws, options));
        }

        // At this point we should have the whole solution converted,
        // so we can build a semantic model and perform global rewrites.
        if (!options.SkipRefinement) {
            AnsiConsole.MarkupLine("[yellow]Refining...[/]");

            // Reset the rewriter sequence counter for this fixup phase
            RewriterSequenceContext.Reset();

            bool hasChanges;
            int run = 0;
            do {
                hasChanges = false;

                IEnumerable<RefinementPipeline> GetRefinementPipelines()
                {
                    RefinementPipeline Pipeline(string name, Func<Document, SemanticModel, Task<LoggedRewriter>> rewriter) => new(ws, options, name, rewriter);

                    if (run == 0) {
                        yield return Pipeline("Converting VB6 Functions", async (t, sem) => new VBCoreRewriter(t.FilePath));
                        yield return Pipeline("Converting Err.Raise to throw", async (t, sem) => new ErrRaiseRewriter(t.FilePath));
                        yield return Pipeline("Converting Err object usage", async (t, sem) => new ErrObjectRewriter(t.FilePath));

                        yield return Pipeline("Converting Cursor Changes", async (t, sem) => new CursorRewriter(t.FilePath));

                        yield return Pipeline("Using control singletons", async (t, sem) => new ControlInstanceRewriter(ws.GetForms(), t.Name));
                        yield return Pipeline("Expanding FRX-backed indexed designer assignments", async (t, sm) => new FrxExpansionRewriter(sm));
                        yield return Pipeline("Fixing Foreach Variable", async (t, sm) => new ForEachVariableRewriter(sm));
                    }

                    yield return Pipeline("Finding Types", async (t, sm) => new TypeFinder(sm, preferredNamespaces));
                    yield return Pipeline("Finding Members", async (t, sm) => new MemberFinder(sm));

                    yield return Pipeline("Expanding default members", async (t, sm) => new DefaultMemberRewriter(sm));

                    yield return Pipeline("Disambiguating Array Access", async (t, sm) => new ArrayCallDisambiguator(sm));
                    yield return Pipeline("Binding property getters to functions", async (t, sm) => new ParameterlessMethodCallRewriter(sm));
                    yield return Pipeline("Binding parameterized property setters", async (t, sm) => new ParameterizedPropertyRewriter(sm));

                    yield return Pipeline("Refining Variable Types", async (t, sm) => {
                        var varTypes = new ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>();
                        await TypeRefiner.GetAllVariablesAndUsages(varTypes, sm, ws.Project.Solution);
                        return new TypeRefiner(varTypes);
                    });

                    yield return Pipeline("Refining Array Declarations", async (t, sm) => {
                        var declaratorTypes = new Dictionary<VariableDeclaratorSyntax, ArrayTypeSyntax>();
                        var symbolTypes = new Dictionary<ISymbol, ArrayTypeSyntax>(SymbolEqualityComparer.Default);
                        await ArrayRefinementRewriter.GetAllArrayVariablesAndUsages(sm, ws.Project.Solution, declaratorTypes, symbolTypes);
                        return new ArrayRefinementRewriter(sm, declaratorTypes, symbolTypes);
                    });

                    yield return Pipeline("Refining Return Types", async (t, sm) => new PropertyTypeRefiner(sm));

                    yield return Pipeline("Coercing Literals", async (t, sm) => new LiteralCoercionRewriter(sm));
                    yield return Pipeline("Casting Enums to Numbers", async (t, sm) => new EnumToNumberCastRewriter(sm));
                    yield return Pipeline("Adding Type Casts", async (t, sm) => new TypeCastRewriter(sm));
                    yield return Pipeline("Applying Type Conversions", async (t, sm) => new TypeConversionRewriter(sm));
                    yield return Pipeline("Converting non-constant const fields to static readonly", async (t, sm) => new ConstFieldRewriter(sm));

                    yield return Pipeline("Rewriting bitwise Or/And", async (t, sm) => new BitwiseOrRewriter(sm));
                    yield return Pipeline("Rewriting DateTime arithmetic", async (t, sm) => new DateTimeArithmeticRewriter(sm));

                    // Cosmetic - shouldn't change program meaning or reduce errors
                    //await RunRewriter("Collapsing local declaration + first assignment", async (t, sm) => new LocalDeclarationCollapseRewriter(sm));

                    yield return Pipeline("Rewriting null checks", async (t, sm) => new DefaultToNullRewriter(sm));
                    yield return Pipeline("Removing unneeded returns", async (t, sm) => new UnneededReturnRewriter(t.FilePath));

                    if (run == 0) {
                        yield return Pipeline("Declaring undeclared local variables", async (t, sm) => new LocalDeclarationInsertionRewriter(sm));
                        yield return Pipeline("Hoisting out-of-scope local declarations", async (t, sm) => new LocalDeclarationHoistingRewriter(sm));
                    }
                }

                foreach (var pipeline in GetRefinementPipelines()) {
                    var result = await RunPipeline(pipeline, repeatUntilStable: true);
                    hasChanges |= result.MadeChanges;
                }

                if (hasChanges) {
                    run++;
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Changes were made, re-running refinement ({run})...[/]");
                }
            }
            while (hasChanges);
        }
    }


}

