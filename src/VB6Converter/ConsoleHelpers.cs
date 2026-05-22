using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VB6Converter;

public static class ConsoleHelpers
{
    public static Task RunOperations(
        string oper, IReadOnlyCollection<ConversionTarget> targets,
        Func<ConversionTarget, ProgressTask, CancellationToken, Task> task)
        => AnsiConsole.Progress()
            .HideCompleted(true)
            .AutoClear(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new ElapsedTimeColumn()
            )
            .StartAsync(async ctx => {
                var overallTask = ctx.AddTask(oper);
                overallTask.MaxValue = targets.Count;
                await Parallel.ForEachAsync(targets, async (target, cancel) => {
                    var progress = ctx.AddTask(target.Name);
                    progress.IsIndeterminate = true;

                    try {
                        await task(target, progress, cancel);
                    }
                    catch (Exception ex) when (!Debugger.IsAttached) {
                        Log.ForFile(target.Name).Error(ex, "{file}: {operation} failed.");
                        progress.Description = $"[red]Failed: {ex.Message}[/]";
                    }
                    finally {
                        progress.StopTask();
                        overallTask.Increment(1);
                    }
                });
            });

    public static async Task<bool> RunOperations(
        string oper, IReadOnlyCollection<ConversionTarget> targets,
        Func<ConversionTarget, ProgressTask, CancellationToken, ValueTask<bool>> task)
    {
        int changed = 0;

        await RunOperations(oper, targets, async (target, progress, cancel) => {
            var res = await task(target, progress, cancel);
            progress.Value = progress.MaxValue;

            if (res) {
                Interlocked.Increment(ref changed);
                progress.Description = $"[green]{target.Name}[/]";
                await Task.Delay(1000, cancel);
            }
        });

        AnsiConsole.WriteLine($"{oper}");
        if (changed > 0) {
            AnsiConsole.MarkupLineInterpolated($"[cyan]  {changed} files changed.[/]");
        }
        else {
            AnsiConsole.WriteLine("  0 files changed.");
        }

        return changed > 0;
    }
}
