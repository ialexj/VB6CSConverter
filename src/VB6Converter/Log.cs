using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using Serilog.Sinks.Spectre;
using System;
using System.IO;

namespace VB6Converter;

internal static class Log
{
    const int AsyncQueueSize = 65_536;

    public static void Init(string outputDir)
    {
        Default = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
            .WriteTo.Spectre()
            .CreateLogger();

        Conversion = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
            .WriteTo.Spectre(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Async(cfg => cfg.File(
                Path.Combine(outputDir, "_Conversion.log"),
                buffered: true),
                bufferSize: AsyncQueueSize,
                blockWhenFull: true)
            .CreateLogger();

        Rewriting = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
            .WriteTo.Async(cfg => cfg.File(
                Path.Combine(outputDir, "_Rewriting.log"),
                outputTemplate: "{Message:lj}{NewLine}",
                buffered: true),
                bufferSize: AsyncQueueSize,
                blockWhenFull: true)
            .CreateLogger();
    }

    public static void Shutdown()
    {
        (Rewriting as IDisposable)?.Dispose();
        (Conversion as IDisposable)?.Dispose();
        (Default as IDisposable)?.Dispose();
        global::Serilog.Log.CloseAndFlush();
    }


    public static ILogger Default { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger Conversion { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger Rewriting { get; private set; } = new LoggerConfiguration().CreateLogger();

    public static ILogger ForFile(string file) => Default.ForContext("file", file);



    public static ILogger ForTree(this ILogger logger, SyntaxTree tree) => logger.ForContext("file", Path.GetFileNameWithoutExtension(tree.FilePath));

    public static ILogger ForNode(this ILogger logger, SyntaxNode node) => logger.ForTree(node.SyntaxTree).ForContext("node", node);

    public static ILogger ForRewriter(this ILogger logger, string rewriter) => logger.ForContext("rewriter", rewriter);
}
