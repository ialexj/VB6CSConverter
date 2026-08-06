using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace ComWrapperGen;

public static class Program
{
    public class CommandLineOptions
    {
        public required string Lib { get; init; }

        public required string OutputDir { get; init; }
    }

    public static async Task<int> Main(string[] args)
    {
        var libOpt = new Option<string>("--lib", ["-l"]) {
            Description = "Library to convert. Either a name, GUID or path. The library must be registered.",
            Required = true
        };
        var outputOpt = new Option<string>("--output", ["-o"]) {
            Description = "Output directory for generated project.",
            Required = true,
        };

        var rootCommand = new RootCommand("Generate C# wrapper project for a COM type library.") {
            libOpt,
            outputOpt
        };

        rootCommand.SetAction(async (ParseResult result) => await Run(new CommandLineOptions {
            Lib = result.GetRequiredValue<string>(libOpt),
            OutputDir = result.GetRequiredValue<string>(outputOpt)
        }));

        return await rootCommand.Parse(args).InvokeAsync();
    }

    public static Task<int> Run(CommandLineOptions options)
    {
        // Implementation of the Run method goes here.
        throw new NotImplementedException();
    }
}
