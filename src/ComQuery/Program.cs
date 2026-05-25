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

namespace ComQuery;

public static class Program
{
    public class CommandLineOptions
    {
        [Option("lib", Required = false, HelpText = "Library to query (name, GUID in {braces}, or file path), optionally followed by ,major,minor for exact version. Repeatable. Includes type information.")]
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
                    var models = TypeLibraryInspector.InspectAll(entry.Guid, entry.Major, entry.Minor, entry.Name, entry.Path, entry.IsTransitive);
                    if (models.Count == 0) {
                        anyFailed = true;
                        return;
                    }

                    foreach (var model in models) {
                        var m = model;

                        // Optionally filter types
                        if (typeFilters.Count > 0 && m.Types != null) {
                            var filtered = m.Types.Where(t => typeFilters.Any(f =>
                                string.Equals(t.Name, f, StringComparison.OrdinalIgnoreCase))).ToList();
                            m = m with { Types = filtered };
                        }

                        results.Add(m);

                        // Enqueue transitive dependencies
                        if (m.DiscoveredDependencies != null) {
                            foreach (var dep in m.DiscoveredDependencies.Where(d => !seenGuids.ContainsKey(d.Guid))) {
                                var depPath = TypeLibraryInspector.ResolveTypeLibPath(dep.Guid, dep.Major, dep.Minor);
                                if (depPath != null) {
                                    var depName = dep.Guid.ToString("B");
                                    toInspect.Enqueue((dep.Guid, dep.Major, dep.Minor, depName, depPath, true));
                                }
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

        // Strip optional version suffix: {base},{major},{minor}
        TryParseVersionSuffix(filter, out string core, out int major, out int minor);

        // GUID?
        if (Guid.TryParseExact(core.Trim('{', '}'), "D", out var guid)
            || Guid.TryParseExact(core, "B", out guid)) {
            var path = TypeLibraryInspector.ResolveTypeLibPath(guid, major, minor);
            if (path != null)
                yield return (guid, major, minor, guid.ToString("B"), path, false);
            yield break;
        }

        // Name: search registry
        foreach (var lib in RegistryEnumerator.EnumerateRegisteredLibraries()) {
            if (lib.Path == null) continue;
            if (!lib.Name.Contains(core, StringComparison.OrdinalIgnoreCase))
                continue;
            // When a version was specified, restrict to that exact version.
            if (major != 0 && (lib.Major != major || lib.Minor != minor))
                continue;
            yield return (lib.Guid, lib.Major, lib.Minor, lib.Name, lib.Path, false);
        }
    }

    /// <summary>
    /// Extracts a trailing <c>,major,minor</c> version suffix from a lib-filter string.
    /// Returns <see langword="true"/> when a valid suffix was found; in that case
    /// <paramref name="core"/> is the filter without the suffix and
    /// <paramref name="major"/>/<paramref name="minor"/> are the parsed integers.
    /// Returns <see langword="false"/> when no version suffix is present, and sets
    /// <paramref name="core"/> to the original <paramref name="filter"/> unchanged.
    /// </summary>
    static bool TryParseVersionSuffix(string filter, out string core, out int major, out int minor)
    {
        core = filter;
        major = minor = 0;

        var parts = filter.Split(',');
        if (parts.Length < 3) return false;

        if (!int.TryParse(parts[^1], out int parsedMinor)) return false;
        if (!int.TryParse(parts[^2], out int parsedMajor)) return false;

        major = parsedMajor;
        minor = parsedMinor;
        core = string.Join(',', parts[..^2]);
        return true;
    }
}
