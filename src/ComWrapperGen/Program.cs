using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;

namespace ComWrapperGen;

public static class Program
{
    public class CommandLineOptions
    {
        public required string Lib { get; init; }

        public required string OutputDir { get; init; }

        public bool Force { get; init; }
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
        var forceOpt = new Option<bool>("--force") {
            Description = "Overwrite existing generated files in the output directory."
        };

        var rootCommand = new RootCommand("Generate C# wrapper project for a COM type library.") {
            libOpt,
            outputOpt,
            forceOpt
        };

        rootCommand.SetAction(async (ParseResult result) => await Run(new CommandLineOptions {
            Lib = result.GetRequiredValue<string>(libOpt),
            OutputDir = result.GetRequiredValue<string>(outputOpt),
            Force = result.GetValue(forceOpt)
        }));

        return await rootCommand.Parse(args).InvokeAsync();
    }

    public static Task<int> Run(CommandLineOptions options)
    {
        if (!OperatingSystem.IsWindows()) {
            Console.Error.WriteLine("ComWrapperGen only supports Windows COM type libraries.");
            return Task.FromResult(1);
        }

        try {
            var resolved = ComLibraryResolver.Resolve(options.Lib);
            var reference = new ComReference(
                Guid: resolved.Guid,
                MajorVersion: resolved.Major,
                MinorVersion: resolved.Minor,
                Lcid: 0,
                Description: resolved.Name,
                IsTransitive: false);

            var library = TypeLibraryInspector.Inspect(reference, resolved.Path);
            if (library is null) {
                Console.Error.WriteLine($"Failed to inspect COM library '{options.Lib}'.");
                return Task.FromResult(1);
            }

            string outputDir = Path.GetFullPath(options.OutputDir);
            WrapperProjectGenerator.Generate(library, outputDir, options.Force);

            Console.WriteLine($"Generated COM wrapper project for '{library.Name}' in '{outputDir}'.");
            return Task.FromResult(0);
        }
        catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
    }
}
