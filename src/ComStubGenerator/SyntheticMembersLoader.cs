#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComStubGenerator;

/// <summary>
/// Loads <see cref="SyntheticMemberSet"/> definitions from a JSON file.
/// </summary>
public static class SyntheticMembersLoader
{
    const string ConventionFileName = "synthetic_members.json";

    static readonly JsonSerializerOptions JsonOptions = new() {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads synthetic member definitions from a JSON file.
    /// </summary>
    /// <remarks>
    /// If <paramref name="explicitPath"/> is provided, that file is used and a
    /// <see cref="FileNotFoundException"/> is thrown if it does not exist.
    /// Otherwise the conventional path (<c>synthetic_members.json</c> in the executable
    /// folder) is checked; if absent, an empty list is returned silently.
    /// </remarks>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <paramref name="explicitPath"/> is specified but does not exist.
    /// </exception>
    public static IReadOnlyList<SyntheticMemberSet> Load(string? explicitPath)
    {
        string path;
        bool required;

        if (explicitPath != null) {
            path = explicitPath;
            required = true;
        }
        else {
            path = Path.Combine(AppContext.BaseDirectory, ConventionFileName);
            required = false;
        }

        if (!File.Exists(path)) {
            if (required)
                throw new FileNotFoundException($"Synthetic members file not found: {path}", path);

            return [];
        }

        Log.Default.Information("SyntheticMembersLoader: loading {path}", path);

        string json = File.ReadAllText(path);
        var sets = JsonSerializer.Deserialize<SyntheticMemberSet[]>(json, JsonOptions) ?? [];

        Log.Default.Information("SyntheticMembersLoader: loaded {count} synthetic member set(s)", sets.Length);

        return sets;
    }
}
