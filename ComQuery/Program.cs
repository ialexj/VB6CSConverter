#nullable enable
using CommandLine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VB6Parser;

namespace ComQuery;

public static class Program
{
    public class CommandLineOptions
    {
        [Option("lib", Required = false, HelpText = "Library to query (name, GUID in {braces}, or file path). Repeatable. Includes type information.")]
        public IEnumerable<string> Libs { get; set; } = [];

        [Option("type", Required = false, HelpText = "Filter to types matching name or GUID within queried libraries. Repeatable.")]
        public IEnumerable<string> Types { get; set; } = [];
    }

    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> Main(string[] args)
    {
        // No args → shallow list of all registered libraries
        if (args.Length == 0) {
            if (!OperatingSystem.IsWindows()) {
                Console.Error.WriteLine("[WARN] COM registry enumeration is only supported on Windows.");
                Console.WriteLine("[]");
                return 0;
            }

            var libs = RegistryEnumerator.EnumerateRegisteredLibraries().ToList();
            Console.WriteLine(JsonSerializer.Serialize(libs, JsonOptions));
            return 0;
        }

        var parsed = Parser.Default.ParseArguments<CommandLineOptions>(args);
        if (parsed.Errors.Any()) return 2;

        if (!OperatingSystem.IsWindows()) {
            Console.Error.WriteLine("[WARN] COM type library inspection is only supported on Windows.");
            Console.WriteLine("[]");
            return 0;
        }

        return await Run(parsed.Value);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static async Task<int> Run(CommandLineOptions options)
    {
        var libFilters = options.Libs.ToList();
        var typeFilters = options.Types.ToList();

        if (libFilters.Count == 0) {
            // No --lib args: shallow list mode
            var all = RegistryEnumerator.EnumerateRegisteredLibraries().ToList();
            Console.WriteLine(JsonSerializer.Serialize(all, JsonOptions));
            return 0;
        }

        // With --lib: full inspection mode
        var results = new ConcurrentBag<ComQueryLibrary>();
        var seenGuids = new ConcurrentDictionary<Guid, bool>();
        bool anyFailed = false;

        // Resolve each filter to a set of (guid, major, minor, name, path) tuples
        var toInspect = new ConcurrentQueue<(Guid Guid, int Major, int Minor, string Name, string Path, bool IsTransitive)>();

        foreach (var filter in libFilters) {
            foreach (var entry in ResolveLibFilter(filter)) {
                toInspect.Enqueue(entry);
            }
        }

        while (!toInspect.IsEmpty) {
            var batch = new List<(Guid Guid, int Major, int Minor, string Name, string Path, bool IsTransitive)>();
            while (toInspect.TryDequeue(out var item)) batch.Add(item);

            await Parallel.ForEachAsync(batch, async (entry, cancel) => {
                if (!seenGuids.TryAdd(entry.Guid, true)) return;

                await Task.Yield();

                try {
                    var model = TypeLibraryInspector.Inspect(entry.Guid, entry.Major, entry.Minor, entry.Name, entry.Path, entry.IsTransitive);
                    if (model == null) {
                        anyFailed = true;
                        return;
                    }

                    // Optionally filter types
                    if (typeFilters.Count > 0 && model.Types != null) {
                        var filtered = model.Types.Where(t => typeFilters.Any(f =>
                            string.Equals(t.Name, f, StringComparison.OrdinalIgnoreCase))).ToList();
                        model = model with { Types = filtered };
                    }

                    results.Add(model);

                    // Enqueue transitive dependencies
                    if (model.DiscoveredDependencies != null) {
                        foreach (var dep in model.DiscoveredDependencies.Where(d => !seenGuids.ContainsKey(d.Guid))) {
                            var depPath = VisualBasicProject.ResolveTypeLibPath(dep.Guid, dep.Major, dep.Minor);
                            if (depPath != null) {
                                var depName = dep.Guid.ToString("B");
                                toInspect.Enqueue((dep.Guid, dep.Major, dep.Minor, depName, depPath, true));
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    anyFailed = true;
                    Log.Warning($"Failed inspecting {entry.Path}", ex);
                }
            });
        }

        var sorted = results.OrderBy(l => l.IsTransitive).ThenBy(l => l.Name).ToList();
        Console.WriteLine(JsonSerializer.Serialize(sorted, JsonOptions));
        return anyFailed ? 1 : 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static IEnumerable<(Guid Guid, int Major, int Minor, string Name, string Path, bool IsTransitive)> ResolveLibFilter(string filter)
    {
        // Path?
        if (File.Exists(filter)) {
            yield return (Guid.Empty, 0, 0, Path.GetFileNameWithoutExtension(filter), filter, false);
            yield break;
        }

        if (filter.Contains(Path.DirectorySeparatorChar) || filter.Contains('/')) {
            Log.Warning($"Path-like filter does not exist: {filter}");
            yield break;
        }

        // GUID?
        if (Guid.TryParseExact(filter.Trim('{', '}'), "D", out var guid)
            || Guid.TryParseExact(filter, "B", out guid)) {
            var path = VisualBasicProject.ResolveTypeLibPath(guid, 0, 0);
            if (path != null)
                yield return (guid, 0, 0, guid.ToString("B"), path, false);
            yield break;
        }

        // Name: search registry
        foreach (var lib in RegistryEnumerator.EnumerateRegisteredLibraries()) {
            if (lib.Path == null) continue;
            if (lib.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || lib.SafeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                yield return (lib.Guid, lib.Major, lib.Minor, lib.Name, lib.Path, false);
        }
    }
}
