using Serilog;

namespace VB6Converter;
internal class Log
{
    public static LoggerConfiguration Configuration = new LoggerConfiguration()
        .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
        .WriteTo.Console();

    public static ILogger Default = Configuration.CreateLogger();
}
