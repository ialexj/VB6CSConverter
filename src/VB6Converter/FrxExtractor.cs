using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VB6Parser;

namespace VB6Converter;

/// <summary>An extracted resource entry produced by <see cref="FrxExtractor"/>.</summary>
public sealed record FrxResourceEntry
{
    /// <summary>Path to the written resource file, relative to the output directory. Null for string blobs and unknown formats.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResourceFilePath { get; init; }

    /// <summary>Format of the extracted blob.</summary>
    public FrxFormat Format { get; init; }

    /// <summary>Strings extracted from a string-blob property (e.g. ListBox.List). Only populated when <see cref="Format"/> is <see cref="FrxFormat.StringBlob"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Strings { get; init; }
}

/// <summary>
/// Pre-conversion FRX extraction stage. Walks every .frx file that accompanies a Form or
/// Control source file, extracts all blobs, writes image/icon/cursor blobs as individual
/// files under <c>_Resources/</c>, and writes a <c>_Resources/_resources.json</c> index
/// so that <see cref="Load"/> can reload the results without re-extracting.
///
/// Dictionary key: lowercase <c>"{stem}.frx:{hexOffset}"</c>, e.g. <c>"frmMain.frx:0000"</c>.
/// </summary>
public sealed class FrxExtractor
{
    private readonly Dictionary<string, FrxResourceEntry> _entries;

    private FrxExtractor(Dictionary<string, FrxResourceEntry> entries) => _entries = entries;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up the resource for the given FRX filename (e.g. <c>"frmMain.frx"</c>) and
    /// hex offset string (e.g. <c>"0000"</c>).  Returns <see langword="null"/> when not found.
    /// </summary>
    public FrxResourceEntry? GetResource(string frxFilename, string hexOffset)
    {
        var key = MakeKey(frxFilename, hexOffset);
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    // ── Factory methods ───────────────────────────────────────────────────

    /// <summary>
    /// Extracts all FRX blobs for the given form/control targets, writes files to
    /// <paramref name="resourcesDir"/>, writes <c>_resources.json</c>, and returns a
    /// populated <see cref="FrxExtractor"/>.
    /// </summary>
    public static FrxExtractor Extract(IEnumerable<ConversionTarget> formTargets, string resourcesDir)
    {
        Directory.CreateDirectory(resourcesDir);

        var entries = new Dictionary<string, FrxResourceEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in formTargets) {
            if (target.File.Type is not (VisualBasicFileType.Form or VisualBasicFileType.Control))
                continue;

            var frxPath = Path.ChangeExtension(target.File.Path, ".frx");
            if (!File.Exists(frxPath))
                continue;

            byte[] frxData;
            try {
                frxData = File.ReadAllBytes(frxPath);
            }
            catch {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(frxPath).ToLowerInvariant();
            var formName = target.Name;

            foreach (var blob in FrxReader.ReadAll(frxData)) {
                var hexOffset = blob.Offset.ToString("X4");
                var key = MakeKey(stem + ".frx", hexOffset);

                if (entries.ContainsKey(key))
                    continue;

                FrxResourceEntry entry;

                if (blob.Format == FrxFormat.StringBlob) {
                    entry = new FrxResourceEntry {
                        Format = FrxFormat.StringBlob,
                        Strings = blob.Strings
                    };
                }
                else if (blob.Format is FrxFormat.Unknown or FrxFormat.OleObject) {
                    entry = new FrxResourceEntry { Format = blob.Format };
                }
                else {
                    var ext = FrxReader.GetExtension(blob.Format);
                    var filename = $"{formName}.0x{hexOffset}.{ext}";
                    var filePath = Path.Combine(resourcesDir, filename);

                    try {
                        File.WriteAllBytes(filePath, blob.Data);
                    }
                    catch {
                        entry = new FrxResourceEntry { Format = FrxFormat.Unknown };
                        entries[key] = entry;
                        continue;
                    }

                    // Store a relative path (portable across machines)
                    var relativePath = Path.Combine("_Resources", filename);
                    entry = new FrxResourceEntry {
                        Format = blob.Format,
                        ResourceFilePath = relativePath
                    };
                }

                entries[key] = entry;
            }
        }

        WriteJson(entries, resourcesDir);
        return new FrxExtractor(entries);
    }

    /// <summary>
    /// Loads a previously extracted <c>_resources.json</c> from <paramref name="resourcesDir"/>
    /// and returns a populated <see cref="FrxExtractor"/>. Returns an empty extractor if the
    /// file does not exist.
    /// </summary>
    public static FrxExtractor Load(string resourcesDir)
    {
        var jsonPath = Path.Combine(resourcesDir, "_resources.json");
        if (!File.Exists(jsonPath))
            return new FrxExtractor(new Dictionary<string, FrxResourceEntry>(StringComparer.OrdinalIgnoreCase));

        try {
            var json = File.ReadAllText(jsonPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, FrxResourceEntry>>(json, JsonOptions)
                       ?? new Dictionary<string, FrxResourceEntry>();
            return new FrxExtractor(new Dictionary<string, FrxResourceEntry>(dict, StringComparer.OrdinalIgnoreCase));
        }
        catch {
            return new FrxExtractor(new Dictionary<string, FrxResourceEntry>(StringComparer.OrdinalIgnoreCase));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string MakeKey(string frxFilename, string hexOffset)
        => $"{frxFilename.ToLowerInvariant()}:{hexOffset.ToUpperInvariant()}";

    private static void WriteJson(Dictionary<string, FrxResourceEntry> entries, string resourcesDir)
    {
        var jsonPath = Path.Combine(resourcesDir, "_resources.json");
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(jsonPath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
