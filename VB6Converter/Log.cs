using Microsoft.CodeAnalysis;
using Serilog;
using System.IO;

namespace VB6Converter;
internal class Log
{
    public static LoggerConfiguration Configuration = new LoggerConfiguration()
        .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
        .WriteTo.Console();

    public static ILogger Default = Configuration.CreateLogger();

    public static ILogger ForFile(string file) => Default.ForContext("file", file);
    
    public static ILogger ForTree(SyntaxTree tree) => Default.ForContext("file", Path.GetFileNameWithoutExtension(tree.FilePath));
}
