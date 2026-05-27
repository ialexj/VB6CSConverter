using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

/// <summary>
/// Scans the entire control-properties tree of a form for FRX references and
/// builds a map of <c>(filename, startOffset) → byteLength</c> by sorting the
/// offsets and computing consecutive differences, with the last entry extending
/// to end-of-file.
/// </summary>
internal static class FrxOffsetScanner
{
    /// <summary>
    /// Returns a dictionary keyed by <c>(frxFilename, startOffset)</c> whose
    /// value is the <c>byteLength</c> for that item.
    /// Returns an empty dictionary when <paramref name="sourceDirectory"/> is
    /// null/empty or no FRX references are found.
    /// </summary>
    public static IReadOnlyDictionary<(string filename, int offset), int> BuildOffsetMap(
        ControlPropertiesContext root,
        string sourceDirectory)
    {
        if (string.IsNullOrEmpty(sourceDirectory))
            return new Dictionary<(string, int), int>();

        // Collect all (filename, hexOffset) pairs from the entire tree.
        var refs = new List<(string filename, int offset)>();
        CollectRefs(root.cp_Properties(), refs);

        if (refs.Count == 0)
            return new Dictionary<(string, int), int>();

        // Group by filename and compute lengths within each file.
        var result = new Dictionary<(string, int), int>();
        foreach (var group in refs.GroupBy(r => r.filename, StringComparer.OrdinalIgnoreCase)) {
            var frxPath = Path.Combine(sourceDirectory, group.Key);
            if (!File.Exists(frxPath))
                continue;

            var fileSize = (int)new FileInfo(frxPath).Length;
            var offsets = group.Select(r => r.offset).Distinct().OrderBy(o => o).ToArray();

            for (var i = 0; i < offsets.Length; i++) {
                var start = offsets[i];
                var end = i + 1 < offsets.Length ? offsets[i + 1] : fileSize;
                var length = end - start;
                if (length >= 0)
                    result[(group.Key, start)] = length;
            }
        }

        return result;
    }

    private static void CollectRefs(IEnumerable<Cp_PropertiesContext> properties, List<(string, int)> refs)
    {
        foreach (var prop in properties) {
            if (prop.cp_SingleProperty() is Cp_SinglePropertyContext single) {
                if (single.FRX_OFFSET() is { } frxToken
                    && single.cp_PropertyValue()?.literal() is { } frxLiteral) {

                    // The string literal contains the filename (e.g. "Form1.frx")
                    var raw = frxLiteral.GetText();
                    // Strip surrounding quotes
                    var filename = raw.Length >= 2 ? raw[1..^1] : raw;

                    var hex = frxToken.GetText().TrimStart(':');
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var offsetVal))
                        refs.Add((filename, offsetVal));
                }
            }
            else if (prop.cp_NestedProperty() is Cp_NestedPropertyContext nested) {
                CollectRefs(nested.cp_Properties(), refs);
            }
            else if (prop.controlProperties() is ControlPropertiesContext child) {
                CollectRefs(child.cp_Properties(), refs);
            }
        }
    }
}
