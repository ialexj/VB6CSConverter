using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.Spectre;
using System;
using System.Diagnostics;
using System.IO;

namespace VB6Converter;

internal static class Log
{
    public static void Init(string outputDir)
    {
        Default = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
            .WriteTo.Spectre()
            .CreateLogger();

        Conversion = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
            .WriteTo.Spectre(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(Path.Combine(outputDir, "_Conversion.log"))
            .CreateLogger();

        Rewriting = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
            .WriteTo.File(Path.Combine(outputDir, "_Rewriting.log"), outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{file}] {rewriter} {Message:lj}{NewLine}")
            .CreateLogger();
    }



    public static ILogger Default { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger Conversion { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger Rewriting { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger ForFile(string file) => Default.ForContext("file", file);



    public static ILogger ForTree(this ILogger logger, SyntaxTree tree) => logger.ForContext("file", Path.GetFileNameWithoutExtension(tree.FilePath));

    public static ILogger ForNode(this ILogger logger, SyntaxNode node) => logger.ForTree(node.SyntaxTree).ForContext("node", node);

    public static ILogger ForRewriter(this ILogger logger, string rewriter) => logger.ForContext("rewriter", rewriter);

    public static SyntaxNode Rewrite<T>(object rewriter, T node, Func<T, SyntaxNode> change, Func<SyntaxNode, object> value = null) where T : SyntaxNode
    {
        var log = Rewriting
            .ForContext("file", Path.GetFileNameWithoutExtension(node.SyntaxTree.FilePath))
            .ForContext("rewriter", rewriter.GetType().Name)
            .ForContext("node", node);

        try {
            var @new = change(node);

            if (log.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                var oldValue = value?.Invoke(node) ?? node;
                var newValue = value?.Invoke(@new) ?? @new;

                if (!RoslynHelpers.IsEquivalentSyntax(oldValue, newValue)) {
                    log.ForContext("node", oldValue).ForContext("changed", newValue).Verbose("{node} --> {changed}");
                }
            }

            return @new;
        }
        catch (Exception ex) when (!Debugger.IsAttached) {
            log.ForContext("error", ex.Message)
                .ForContext("method", ex.TargetSite.Name)
                .Error("Failed to rewrite {node} ({method}): {error:nq}");
            
            throw;
        }
    }
}
