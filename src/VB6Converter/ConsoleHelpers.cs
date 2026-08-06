using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VB6Converter.Rewriters;
using VB6Parser;
using static VB6Converter.Program;

namespace VB6Converter;

public interface IOperation
{
    string Name { get; }

    bool Changed { get; }

    Task Run(ProgressTask progress, int iteration, CancellationToken cancel);
}

public interface IPipeline
{
    string Name { get; }

    IReadOnlyCollection<IOperation> Operations { get; }

    Task Starting(int iteration);

    Task Completed(int iteration);
}

public record class PipelineResult(IPipeline Pipeline, int Iterations, bool MadeChanges);

public static class ConsoleUI
{
    public static async Task<PipelineResult> RunPipeline(IPipeline pipeline, bool repeatUntilStable = false)
    {
        if (pipeline.Operations.Count == 0) {
            return new PipelineResult(pipeline, 0, false);
        }

        bool madeChanges = false;
        int changed = 0;
        int iteration = 0;
        do {
            iteration++;
            await pipeline.Starting(iteration);

            await AnsiConsole.Progress()
                .HideCompleted(true)
                .AutoClear(true)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new ElapsedTimeColumn()
                )
                .StartAsync(async ctx => {
                    var overallTask = ctx.AddTask(pipeline.Name);
                    overallTask.MaxValue = pipeline.Operations.Count();

                    await Parallel.ForEachAsync(pipeline.Operations, async (target, cancel) => {
                        var progress = ctx.AddTask(target.Name);
                        progress.IsIndeterminate = true;

                        try {
                            await target.Run(progress, iteration, cancel);
                            progress.Value = progress.MaxValue;
                            progress.Description = $"[green]{target.Name}[/]";
                        }
                        catch (Exception ex) when (!Debugger.IsAttached) {
                            Log.ForFile(target.Name).ForContext("operation", pipeline.Name).Error(ex, "{file}: {operation} failed.");
                            progress.Description = $"[red]Failed[/]";
                        }
                        finally {
                            progress.StopTask();
                            overallTask.Increment(1);
                        }
                    });
                });

            AnsiConsole.WriteLine($"{pipeline.Name}");
            changed = pipeline.Operations.Count(op => op.Changed);
            if (changed > 0) {
                madeChanges = true;
                await pipeline.Completed(iteration);
                AnsiConsole.MarkupLineInterpolated($"[cyan]  {changed} changes.[/]");
            }
            else {
                AnsiConsole.WriteLine("  0 files changed.");
            }
        }
        while (repeatUntilStable && changed > 0);

        return new PipelineResult(pipeline, iteration, madeChanges);
    }
}

public abstract class WorkspacePipeline(ConversionWorkspace ws, ConverterOptions options) : IPipeline
{
    protected ConversionWorkspace Workspace { get; } = ws ?? throw new ArgumentNullException(nameof(ws));

    public abstract string Name { get; }

    public abstract IReadOnlyCollection<IOperation> Operations { get; }

    public virtual Task Starting(int iteration) => Task.CompletedTask;

    public virtual async Task Completed(int iteration)
    {
        if (Operations.Any(op => op.Changed)) {
            await Compile();
        }

        if (options.Pause) {
            AnsiConsole.MarkupLine("[grey]Press any key to continue (Ctrl-C to stop)...[/]");
            Console.ReadKey(intercept: true);
        }

        if (options.Git) {
            await GitUtils.Commit(ws.OutputDir, Name + $" (iteration {iteration})");
        }
    }

    protected async Task Compile()
    {
        await ws.ReloadProject(); // load any changes from disk
        await AnsiConsole.Status().StartAsync("Compiling...", ctx => ws.Compile());

        AnsiConsole.Status()
            .Start("Collecting Diagnostics...", ctx => {
                var diagnostics = ws.Compilation.GetDiagnostics();
                var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                if (errorCount > 0) {
                    AnsiConsole.MarkupLineInterpolated($"[red]Errors: {errorCount}[/]");
                }
            });
    }
}

public class ComStubPipeline(ConversionWorkspace ws, ConverterOptions options) : WorkspacePipeline(ws, options)
{
    public override string Name => "Generating COM Stubs";

    public override IReadOnlyCollection<IOperation> Operations { get; } = [ new ComStubOperation(options) ];

    public class ComStubOperation(ConverterOptions options) : IOperation
    {
        public string Name => "Generating COM Stubs";

        public bool Changed { get; private set; } = true;

        public async Task Run(ProgressTask progress, int iteration, CancellationToken cancel)
        {
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

            var referenceDir = Path.Join(options.OutputDir, "_References");

            var psi = new ProcessStartInfo {
                FileName = stubGenExe,
                ArgumentList = { "-p", options.Project, "-o", referenceDir },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var excludeList = options.ExcludeReferences.ToList();
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
}

public class CleanSourcePipeline(ConversionWorkspace ws, ConverterOptions options) : WorkspacePipeline(ws, options)
{
    public override string Name => "Normalizing VB6 Source Files";

    public override IReadOnlyCollection<IOperation> Operations { get; }
        = ws.ActiveTargets.Where(t => !t.Exists)
            .Select(t => new CleanSourceOperation(t))
            .ToList();

    public class CleanSourceOperation(ConversionTarget target) : IOperation
    {
        public string Name => target.Name;

        public bool Changed { get; private set; }

        public async Task Run(ProgressTask progress, int iteration, CancellationToken cancel)
        {
            await VB6ToCSharpConversion.NormalizeSource(target.File.Path);
            Changed = true;
        }
    }
}

public class ConversionPipeline(ConversionWorkspace ws, ConverterOptions options) : WorkspacePipeline(ws, options)
{
    public override string Name => "Converting VB6 to C#";

    public override IReadOnlyCollection<IOperation> Operations { get; }
        = ws.ActiveTargets
            .Where(t => !t.Exists)
            .Select(t => new ConversionOperation(ws, t))
            .ToList();

    public class ConversionOperation(ConversionWorkspace ws, ConversionTarget t) : IOperation
    {
        public string Name => t.Name;

        public bool Changed { get; private set; }

        public async Task Run(ProgressTask progress, int iteration, CancellationToken cancel)
        {
            Changed = await ws.WithCompilationUnit(t, cancel, cu => {
                var conversion = VB6ToCSharpConversion.ConvertFile(
                    t.File.Path, t.OutputPath, t.Name, ws.Name, t.File.Type, ws.Options,
                    t.SourceRelativePath);

                var st = SyntaxFactory.SyntaxTree(conversion.CompilationUnit, path: t.OutputPath);
                return ValueTask.FromResult(st.GetCompilationUnitRoot(cancel));
            });
        }
    }
}

public class SplitDesignerPipeline(ConversionWorkspace ws, ConverterOptions options) : WorkspacePipeline(ws, options)
{
    public override string Name => "Splitting Designer Files";

    public override IEnumerable<IOperation> Operations { get; } = ws.ActiveTargets
        .Where(t => t.File.Type is VisualBasicFileType.Form or VisualBasicFileType.Control)
        .Where(t => !t.DesignerExists)
        .Select(t => new SplitDesignerOperation(ws, t))
        .ToList();

    public override Task Completed(int iteration)
    {
        foreach (var op in Operations.OfType<SplitDesignerOperation>()) {
            if (op.DesignerCompilationUnit is not null) {
                // will get picked up on reload because it's on disk
                //ws.AddDocument(op.DesignerCompilationUnit.SyntaxTree, op.DesignerCompilationUnit.SyntaxTree.FilePath);
            }
        }

        return base.Completed(iteration);
    }

    public class SplitDesignerOperation(ConversionWorkspace ws, ConversionTarget t) : IOperation
    {
        public string Name => t.Name;

        public bool Changed { get; private set; }

        public CompilationUnitSyntax DesignerCompilationUnit { get; private set; }

        public async Task Run(ProgressTask progress, int iteration, CancellationToken cancel)
        {
            Changed = await ws.WithCompilationUnit(t, cancel, cu => {
                var (mainCu, designerCu) = DesignerFileSplitter.Split(cu);
                if (designerCu is not null) {
                    File.WriteAllText(t.DesignerOutputPath, designerCu.NormalizeWhitespace().ToFullString()); // will get picked up on designer reload
                }

                DesignerCompilationUnit = designerCu;
                return ValueTask.FromResult(mainCu);
            });
        }
    }
}

public class RefinementPipeline(ConversionWorkspace ws, ConverterOptions options, string name, Func<Document, SemanticModel, Task<LoggedRewriter>> rewriter) : WorkspacePipeline(ws, options)
{
    public override string Name => name;

    public override async Task Starting(int iteration)
    {
        if (ws.Compilation is null) {
            await Compile();
        }

        await base.Starting(iteration);
    }

    public override IEnumerable<IOperation> Operations { get; } = ws.Documents.Select(d => new RefinementOperation(ws, d, rewriter)).ToList();
}

public class RefinementOperation(ConversionWorkspace ws, Document doc, Func<Document, SemanticModel, Task<LoggedRewriter>> rewriter) : IOperation
{
    public string Name => doc.Name;

    public bool Changed { get; private set; }

    public async Task Run(ProgressTask progress, int iteration, CancellationToken cancel)
    {
        Changed = await ws.WithCompilationUnit(doc, cancel, async cu => {
            var sm = ws.Compilation.GetSemanticModel(cu.SyntaxTree, true);

            var r = await rewriter(doc, sm);
            r.RewriterSequence = RewriterSequenceContext.GetNextSequence();
            r.Progress = (current, total) => {
                progress.IsIndeterminate = false;
                progress.MaxValue = total;
                progress.Value = current;
            };

            return (CompilationUnitSyntax)r.Visit(cu);
        });
    }
}
