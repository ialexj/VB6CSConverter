#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VB6Parser;

namespace VB6Instrument;

/// <summary>
/// One member entry in the instrumentation map: the member kind, its line
/// range in the original (pre-instrumentation) source, and the
/// virtual-to-real map of the Erl numbers it carries.
/// </summary>
public sealed class MapMember
{
    public required string Key { get; init; }

    /// <summary>Member kind as declared: Sub, Function, Property Get/Let/Set.</summary>
    public required string Type { get; init; }

    /// <summary>1-based first physical line (the signature) in the original file.</summary>
    public required int Start { get; init; }

    /// <summary>1-based last physical line (the End statement) in the original file.</summary>
    public required int End { get; init; }

    /// <summary>Total line count of the member (start..end, inclusive).</summary>
    public int Length => End - Start + 1;

    /// <summary>Virtual (Erl) line → real line in the original file.</summary>
    public SortedDictionary<int, int> Lines { get; } = [];
}

/// <summary>
/// One source file entry: its <c>Attribute VB_Name</c> value, file type,
/// and members in declaration order.
/// </summary>
public sealed partial class MapFile
{
    /// <summary>The file's <c>Attribute VB_Name</c> value, or null when the file declares none.</summary>
    public string? Name { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public List<MapMember> Members { get; } = [];

    /// <summary>
    /// Captures the map data for one file from its (already line-numbered)
    /// model. Member ranges are the original file's — pass A never inserts
    /// or deletes lines — and every statement-start line carrying a number
    /// contributes one virtual-to-real entry, whether the number was
    /// assigned by this run or was already present on a skipped member.
    /// </summary>
    /// <param name="model">The parsed file, after pass A ran.</param>
    /// <param name="fileType">Authoritative type override (the .vbp-declared
    /// Module/Class/Form/Control), or null to derive it from the extension.</param>
    public static MapFile Capture(Vb6FileModel model, string? fileType = null)
    {
        var file = new MapFile {
            Name = ReadVbName(model),
            Type = fileType ?? FileTypeFromExtension(model.Extension),
        };

        foreach (var range in model.Members) {
            file.Members.Add(new MapMember {
                Key = MemberKey(range),
                Type = range.Kind,
                Start = range.StartIndex + 1,
                End = range.EndIndex + 1,
            });
        }

        // Map every interior member line to the index of its owning member.
        var ownerOfLine = new int?[model.LineCount];
        for (var memberIndex = 0; memberIndex < model.Members.Count; memberIndex++) {
            var range = model.Members[memberIndex];
            for (var index = range.BodyStartIndex; index <= range.BodyEndIndex; index++) {
                ownerOfLine[index] = memberIndex;
            }
        }

        foreach (var statement in model.Statements) {
            var lineIndex = statement.StartLine - 1;
            var owner = ownerOfLine[lineIndex];
            if (owner is null) {
                continue;
            }
            var numbered = Vb6Patterns.Numbered().Match(model.Contents[lineIndex]);
            if (!numbered.Success) {
                continue;
            }
            file.Members[owner.Value].Lines[int.Parse(numbered.Groups[1].Value, CultureInfo.InvariantCulture)] = statement.StartLine;
        }

        return file;
    }

    /// <summary>Map key for a member: the member name; Property
    /// Get/Let/Set are suffixed with the accessor kind ("Foo.Get") because
    /// the three accessors of one property share a single name.</summary>
    public static string MemberKey(Vb6MemberRange member) =>
        member.Kind.StartsWith("Property", StringComparison.OrdinalIgnoreCase)
            ? $"{member.Name}.{AccessorKind(member.Kind)}"
            : member.Name;

    private static string AccessorKind(string kind)
    {
        var word = kind.Split(' ')[^1];
        return word.ToLowerInvariant() switch {
            "get" => "Get",
            "let" => "Let",
            "set" => "Set",
            _ => word,
        };
    }

    /// <summary>File type for the map, derived from the source extension.
    /// Used when no .vbp declares the type (directory and single-file
    /// targets).</summary>
    public static string FileTypeFromExtension(string extension) => extension.ToLowerInvariant() switch {
        ".bas" => "Module",
        ".cls" => "Class",
        ".frm" => "Form",
        ".ctl" => "Control",
        ".dob" => "UserDocument",
        ".dsr" => "ActiveXDesigner",
        ".pag" => "PropertyPage",
        _ => "Module",
    };

    private static string? ReadVbName(Vb6FileModel model)
    {
        if (model.CodeStartIndex < 0) {
            return null;
        }
        var match = VbNamePattern().Match(model.Contents[model.CodeStartIndex]);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^\s*Attribute\s+VB_Name\s*=\s*""?(.*?)""?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VbNamePattern();
}

/// <summary>
/// The instrumentation map document written as <c>&lt;target&gt;.map</c>
/// next to the instrumentation target: project identity (from the .vbp,
/// when the target is one) and per-file member inventories with the
/// virtual-to-real line maps. JSON keys must be strings, so virtual line
/// numbers appear quoted (<c>"10": 18</c>).
/// </summary>
public sealed class InstrumentationMap
{
    private readonly List<(string Key, MapFile File)> files = [];

    /// <summary>The instrumentation target's file name (e.g. MyProject.vbp).</summary>
    public string? ProjectName { get; set; }

    /// <summary>Binary version declared by the .vbp; null (omitted) when unknown.</summary>
    public VisualBasicProjectVersion? Version { get; set; }

    /// <summary>Descriptive metadata declared by the .vbp; null (omitted) when unknown.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }

    public void AddFile(string key, MapFile file) => files.Add((key, file));

    /// <summary>The map sits next to the instrumentation target, named
    /// after it: <c>VBProject.vbp</c> → <c>VBProject.vbp.map</c>, a single
    /// source file → <c>Module1.bas.map</c>, a directory →
    /// <c>&lt;dirname&gt;.map</c>.</summary>
    public static string GetMapPath(string projectPath)
    {
        if (Directory.Exists(projectPath)) {
            var fullPath = Path.GetFullPath(projectPath);
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
            return Path.Combine(fullPath, name + ".map");
        }
        return projectPath + ".map";
    }

    /// <summary>Renders the indented JSON document: project, then files
    /// (sorted by path key) → members (declaration order) → virtual line
    /// (ascending, as a string key) → real line.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) {
            writer.WriteStartObject();

            writer.WriteStartObject("project");
            if (ProjectName is not null) {
                writer.WriteString("name", ProjectName);
            }
            if (Version is not null) {
                writer.WriteStartObject("version");
                writer.WriteNumber("major", Version.Major);
                writer.WriteNumber("minor", Version.Minor);
                writer.WriteNumber("revision", Version.Revision);
                writer.WriteEndObject();
            }
            if (Metadata is { Count: > 0 }) {
                writer.WriteStartObject("metadata");
                foreach (var (key, value) in Metadata) {
                    writer.WriteString(key, value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            writer.WriteStartObject("files");
            foreach (var (key, file) in files.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)) {
                writer.WriteStartObject(key);
                if (file.Name is not null) {
                    writer.WriteString("name", file.Name);
                }
                writer.WriteString("type", file.Type);
                writer.WriteStartObject("members");
                foreach (var member in file.Members) {
                    writer.WriteStartObject(member.Key);
                    writer.WriteString("type", member.Type);
                    writer.WriteNumber("start", member.Start);
                    writer.WriteNumber("end", member.End);
                    writer.WriteNumber("length", member.Length);
                    writer.WriteStartObject("lines");
                    foreach (var (virtualLine, realLine) in member.Lines) {
                        writer.WriteNumber(virtualLine.ToString(CultureInfo.InvariantCulture), realLine);
                    }
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
