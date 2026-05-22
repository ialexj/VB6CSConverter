#nullable enable
using CommandLine;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VB6Parser;

namespace ComStubGenerator;

public static class Program
{
    public class CommandLineOptions
    {
        [Option('p', "project", Required = false, HelpText = "Path to the VB6 project file (.vbp). Mutually exclusive with --lib.")]
        public string? Project { get; set; }

        [Option("lib", Required = false, HelpText = "Library to generate stubs for (name, GUID, or path). Repeatable. Mutually exclusive with --project.")]
        public IEnumerable<string> Libs { get; set; } = [];

        [Option('o', "output", Required = true, HelpText = "Output directory for generated stub files.")]
        public string OutputDir { get; set; } = null!;

        [Option("arch", Required = false, Default = "both",
            HelpText = "Architecture(s) to query: x86, x64, or both.")]
        public string Arch { get; set; } = "both";

        [Option("include-com-plumbing", Required = false, Default = false,
            HelpText = "Include COM infrastructure members (IUnknown, IDispatch, AddRef, Release, etc.) in generated stubs.")]
        public bool IncludeComPlumbing { get; set; }

        [Option("synthetic-member-path", Required = false,
            HelpText = "Path to a JSON file providing synthetic members to inject into COM types. " +
                       "Defaults to synthetic_members.json in the executable folder.")]
        public string? SyntheticMemberPath { get; set; }
    }

    public static async Task<int> Main(string[] args)
    {
        var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
        if (parsed.Errors.Any()) {
            return 2;
        }

        return await Run(parsed.Value);
    }

    static async Task<int> Run(CommandLineOptions options)
    {
        Directory.CreateDirectory(options.OutputDir);
        Log.Init(options.OutputDir);

        if (!OperatingSystem.IsWindows()) {
            AnsiConsole.MarkupLine("[grey]COM reference stub generation is only supported on Windows.[/]");
            return 2;
        }

        IReadOnlyList<SyntheticMemberSet> syntheticSets;
        try {
            syntheticSets = SyntheticMembersLoader.Load(options.SyntheticMemberPath);
        }
        catch (System.IO.FileNotFoundException ex) {
            AnsiConsole.MarkupLineInterpolated($"[red]Synthetic members file not found: {ex.FileName}[/]");
            return 1;
        }

        var libFilters = new List<string>(options.Libs);

        if (options.Project != null) {
            var vbProject = VisualBasicProject.Load(options.Project);
            if (vbProject.References.Count == 0 && libFilters.Count == 0) {
                AnsiConsole.MarkupLine("[grey]No COM references found in project.[/]");
                return 0;
            }
            foreach (var reference in vbProject.References.Where(r => !DotnetLibraryGuids.Contains(r.Guid)))
                libFilters.Add(reference.Guid.ToString("B"));
        }

        if (libFilters.Count == 0) {
            AnsiConsole.MarkupLine("[grey]No libraries to process.[/]");
            return 0;
        }

        bool anyFailed = await GenerateStubsWindows(libFilters, options.OutputDir, options.Arch, !options.IncludeComPlumbing, syntheticSets);
        return anyFailed ? 1 : 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static async Task<bool> GenerateStubsWindows(
        IEnumerable<string> libFilters,
        string outputDir,
        string arch,
        bool filterComPlumbing,
        IReadOnlyList<SyntheticMemberSet> syntheticSets)
    {
        AnsiConsole.MarkupLine("[yellow]Querying COM type libraries...[/]");

        var libFilterList = libFilters.ToList();

        Task<ComQueryLibrary[]?> x86Task = (arch is "x86" or "both")
            ? ComQueryClient.QueryAsync("x86", libFilterList)
            : Task.FromResult<ComQueryLibrary[]?>([]);

        Task<ComQueryLibrary[]?> x64Task = (arch is "x64" or "both")
            ? ComQueryClient.QueryAsync("x64", libFilterList)
            : Task.FromResult<ComQueryLibrary[]?>([]);

        await Task.WhenAll(x86Task, x64Task);

        var x86Libs = x86Task.Result ?? [];
        var x64Libs = x64Task.Result ?? [];

        if (x86Libs.Length == 0 && x64Libs.Length == 0) {
            AnsiConsole.MarkupLine("[yellow]No type library information returned by ComQuery.[/]");
            return false;
        }

        var merged = LibraryMerger.Merge(x86Libs, x64Libs);

        if (syntheticSets.Count > 0)
            merged = SyntheticMembersApplicator.Apply(merged, syntheticSets);

        int resolved = 0, generated = 0;
        var reportLines = new List<string>();

        foreach (var library in merged) {
            if (library.Types == null || library.Types.Count == 0) continue;
            resolved++;

            var written = ReferenceStubGenerator.Generate(library, outputDir, filterComPlumbing);
            generated += written.Count;

            reportLines.Add($"OK          {library.Name} - {library.Guid} - {library.Path}  ({written.Count} types)");
            AnsiConsole.WriteLine($"  {library.Name}: {written.Count} stubs");
        }

        var directModels = merged.Where(m => !m.IsTransitive).ToList();
        var allAliases = directModels.SelectMany(m => ReferenceStubGenerator.CollectAliases(m));
        var referenceUsingsPath = ReferenceUsingsGenerator.Generate(directModels, outputDir, allAliases);

        var reportPath = Path.Combine(outputDir, "_ReferenceStubs.txt");
        await File.WriteAllLinesAsync(reportPath, new[] {
            $"Reference stubs generated: {generated}",
            $"Libraries resolved:        {resolved}",
            $"Reference usings file:     {referenceUsingsPath}",
            string.Empty,
        }.Concat(reportLines));

        AnsiConsole.MarkupLineInterpolated(
            $"[green]Reference stubs:[/] {generated} types from {resolved} libraries. Report: {reportPath}");

        return false;
    }
}
