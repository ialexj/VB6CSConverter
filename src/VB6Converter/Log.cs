using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;
using Serilog.Core;
using Serilog.Sinks.Spectre;
using System;
using System.Collections.Concurrent;
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
            .WriteTo.File(Path.Combine(outputDir, "_Rewriting.log"), outputTemplate: "{Message:lj}{NewLine}")
            .CreateLogger();

        _rewritingLoggers.Clear();
    }

    static readonly ConcurrentDictionary<string, ILogger> _rewritingLoggers = new(StringComparer.OrdinalIgnoreCase);

    public static ILogger GetRewritingLogger(string csFilePath)
    {
        if (string.IsNullOrEmpty(csFilePath))
            return Rewriting;

        System.Diagnostics.Trace.WriteLine($"Creating logger for {csFilePath}");

        return _rewritingLoggers.GetOrAdd(csFilePath, path => new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
            .WriteTo.File(path + ".rewrite.log", outputTemplate: "{Message:lj}{NewLine}")
            .CreateLogger());
    }

    public static void CloseRewritingLoggers()
    {
        foreach (var logger in _rewritingLoggers.Values) {
            (logger as IDisposable)?.Dispose();
        }
        _rewritingLoggers.Clear();
    }



    public static ILogger Default { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger Conversion { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger Rewriting { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger ForFile(string file) => Default.ForContext("file", file);



    public static ILogger ForTree(this ILogger logger, SyntaxTree tree) => logger.ForContext("file", Path.GetFileNameWithoutExtension(tree.FilePath));

    public static ILogger ForNode(this ILogger logger, SyntaxNode node) => logger.ForTree(node.SyntaxTree).ForContext("node", node);

    public static ILogger ForRewriter(this ILogger logger, string rewriter) => logger.ForContext("rewriter", rewriter);
}
