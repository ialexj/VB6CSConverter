#nullable enable
using System.Text.RegularExpressions;

namespace VB6Instrument;

/// <summary>
/// One member (Sub / Function / Property Get|Let|Set) range in a file.
/// <see cref="StartIndex"/> and <see cref="EndIndex"/> are 0-based physical
/// lines of the signature and the matching End statement; multi-line
/// signatures are folded by the statement tokenizer.
/// </summary>
/// <param name="SignatureEndIndex">0-based last physical line of the
/// signature (continuations folded) — the first candidate line for
/// statement insertions.</param>
public sealed record Vb6MemberRange(
    string Name,
    string Kind,
    string EndKind,
    string Mods,
    int StartIndex,
    int SignatureEndIndex,
    int EndIndex)
{
    /// <summary>0-based first line inside the body (interior lines only;
    /// includes signature continuation lines, matching the incumbent
    /// numbering tool's push-out behaviour).</summary>
    public int BodyStartIndex => StartIndex + 1;

    /// <summary>0-based first line that can carry an inserted statement —
    /// the line after the whole (folded) signature.</summary>
    public int FirstInsertableIndex => SignatureEndIndex + 1;

    /// <summary>0-based last line inside the body (the line before End).</summary>
    public int BodyEndIndex => EndIndex - 1;

    public bool IsInRange(int zeroBasedLine) => zeroBasedLine > StartIndex && zeroBasedLine < EndIndex;
}

public static partial class Vb6MemberScanner
{
    public const string DesignerExtensions = ".frm,.ctl,.pag,.dsr,.dob";
    public const string SourceExtensions = ".bas,.cls,.frm,.ctl,.dob,.dsr,.pag";

    [GeneratedRegex(@"^(?<mods>(?:(?:Public|Private|Friend|Static|Global|Default)\s+)*)(?<kind>Sub|Function|Property\s+(?:Get|Let|Set))\s+(?<name>(?:\[[^\]]+\]|[^\W\d]\w*))", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex MemberStartPattern();

    [GeneratedRegex(@"^End\s+(?<kind>Sub|Function|Property)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex MemberEndPattern();

    /// <summary>
    /// Returns the 0-based line index of the code section start: the
    /// <c>Attribute VB_Name</c> line, or -1 when the file has none (all
    /// lines are then code). Exported designer files must contain it.
    /// </summary>
    public static int GetCodeStartIndex(string[] lines, string extension)
    {
        for (var index = 0; index < lines.Length; index++) {
            if (Regex.IsMatch(lines[index], @"^\s*Attribute\s+VB_Name\s*=", RegexOptions.IgnoreCase)) {
                return index;
            }
        }
        if (IsDesignerExtension(extension)) {
            throw new InstrumentationException(null, "Exported designer files must contain Attribute VB_Name to identify the code section.");
        }
        return -1;
    }

    public static bool IsDesignerExtension(string extension) =>
        DesignerExtensions.Split(',').Contains(extension, StringComparer.OrdinalIgnoreCase);

    public static bool IsSourceExtension(string extension) =>
        SourceExtensions.Split(',').Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds member ranges from the tokenized statements. Fail-closed:
    /// mismatched, unexpected, nested or unterminated members throw, naming
    /// the member and line.
    /// </summary>
    public static List<Vb6MemberRange> ScanMembers(string filePath, List<Vb6Statement> statements)
    {
        var ranges = new List<Vb6MemberRange>();
        Vb6MemberRange? active = null;

        foreach (var statement in statements) {
            var statementText = statement.Text;
            var endMatch = MemberEndPattern().Match(statementText);
            if (endMatch.Success) {
                var kind = NormalizeKind(endMatch.Groups["kind"].Value);
                if (active is null) {
                    throw new InstrumentationException(filePath, $"Unexpected End {kind} at line {statement.StartLine}.");
                }
                if (active.EndKind != kind) {
                    throw new InstrumentationException(filePath, $"Mismatched End {kind} for {active.Kind} {active.Name} at line {statement.StartLine}.");
                }
                ranges.Add(active with { EndIndex = statement.EndLine - 1 });
                active = null;
                continue;
            }

            var startMatch = MemberStartPattern().Match(statementText);
            if (startMatch.Success) {
                if (active is not null) {
                    throw new InstrumentationException(filePath, $"Member '{startMatch.Groups["name"].Value}' at line {statement.StartLine} starts before {active.Kind} '{active.Name}' ends.");
                }
                var kind = NormalizeKind(startMatch.Groups["kind"].Value);
                active = new Vb6MemberRange(
                    startMatch.Groups["name"].Value,
                    kind,
                    kind.Split(' ')[0],
                    startMatch.Groups["mods"].Value,
                    statement.StartLine - 1,
                    statement.EndLine - 1,
                    -1);
            }
        }

        if (active is not null) {
            throw new InstrumentationException(filePath, $"Unterminated {active.Kind} {active.Name} at line {active.StartIndex + 1}.");
        }
        return ranges;
    }

    private static string NormalizeKind(string kind) => Regex.Replace(kind, @"\s+", " ");
}
