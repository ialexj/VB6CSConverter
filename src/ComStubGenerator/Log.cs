using Serilog;
using Serilog.Sinks.Spectre;
using System.IO;

namespace ComStubGenerator;

internal static class Log
{
    public static void Init(string outputDir)
    {
        Default = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
            .WriteTo.Spectre()
            .WriteTo.File(Path.Combine(outputDir, "_StubGen.log"))
            .CreateLogger();
    }

    public static ILogger Default { get; private set; } = new LoggerConfiguration()
        .WriteTo.Spectre()
        .CreateLogger();
}
