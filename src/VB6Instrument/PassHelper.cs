#nullable enable
using System.Text.RegularExpressions;

namespace VB6Instrument;

/// <summary>Outcome of one instrumentation pass over one file.</summary>
public class PassResult
{
    public PassResult(string passName) => PassName = passName;

    public string PassName { get; }

    public int MembersSeen { get; set; }

    public int MembersTouched { get; set; }

    public int MembersSkipped { get; set; }

    public int LinesChanged { get; set; }

    public List<string> Warnings { get; } = [];
}

/// <summary>A new full line inserted before an existing 0-based line index.</summary>
public sealed record LineInsertion(int LineIndex, string Content);

/// <summary>Regexes shared by the passes. PowerShell <c>-match</c> is
/// case-insensitive by default, so the incumbent semantics require
/// <see cref="RegexOptions.IgnoreCase"/> throughout.</summary>
public static partial class Vb6Patterns
{
    /// <summary>"10        If x Then" — number + padding + code.</summary>
    [GeneratedRegex(@"^(\d+)(\s+)(\S.*)$", RegexOptions.Compiled)]
    public static partial Regex Numbered();

    /// <summary>Any leading number ("10" or "10 ").</summary>
    [GeneratedRegex(@"^\d+(\s|$)", RegexOptions.Compiled)]
    public static partial Regex AnyNumber();

    /// <summary>A line starting with a label ("Abortar: ...").</summary>
    [GeneratedRegex(@"^[A-Za-z_]\w*:", RegexOptions.Compiled)]
    public static partial Regex LabelStart();

    /// <summary>A bare label line, optionally with a trailing comment.</summary>
    [GeneratedRegex(@"^[A-Za-z_]\w*:\s*(?:'.*)?$", RegexOptions.Compiled)]
    public static partial Regex LabelOnly();

    /// <summary>Statements that never carry a line number.</summary>
    [GeneratedRegex(@"^(?:Dim|Const|Static|Attribute|Option|Implements|Declare|Event)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex NonNumbered();

    [GeneratedRegex(@"^\s*'", RegexOptions.Compiled)]
    public static partial Regex CommentOnly();

    [GeneratedRegex(@"^\s*Rem(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex RemOnly();

    [GeneratedRegex(@"^Attribute\s+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex AttributeLine();

    [GeneratedRegex(@"^\s*#", RegexOptions.Compiled)]
    public static partial Regex DirectiveLine();

    /// <summary>A line inserted by the error-guard pass (re-runs must skip
    /// it when locating the trace insertion point). Hand-written On Error
    /// statements with other label names do not match and are treated as
    /// ordinary code, matching the incumbent tool.</summary>
    [GeneratedRegex(@"^On Error GoTo GuardError_\w+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex GuardInsertionLine();
}

/// <summary>Shared pass plumbing: insertions, templates, body scanning.</summary>
public static class PassHelper
{
    public const int NaturalBodyIndent = 4;   // one tab in the VB6 editor
    public const int NumberedBodyIndent = 10; // the MZ-Tools numbering baseline

    /// <summary>
    /// Renders a statement template, replacing {File} / {Member}
    /// placeholders case-insensitively.
    /// </summary>
    public static string RenderTemplate(string template, string file, string member) => Regex.Replace(
        template,
        @"\{file\}|\{member\}",
        match => match.Value.Equals("{file}", StringComparison.OrdinalIgnoreCase) ? file : member,
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns the 0-based physical line index the first code-bearing body
    /// statement sits on, skipping blank lines, comments, Rem, attributes,
    /// conditional-compilation directives and lines previously inserted by
    /// the error-guard pass; -1 when the body has no insertable statement.
    /// </summary>
    public static int FindFirstCodeLine(string[] contents, int bodyStartIndex, int bodyEndIndexExclusive)
    {
        for (var index = bodyStartIndex; index < bodyEndIndexExclusive; index++) {
            var line = contents[index];
            if (line.Length == 0) {
                continue;
            }
            if (Vb6Patterns.CommentOnly().IsMatch(line) || Vb6Patterns.RemOnly().IsMatch(line)) {
                continue;
            }
            if (Vb6Patterns.AttributeLine().IsMatch(line) || Vb6Patterns.DirectiveLine().IsMatch(line)) {
                continue;
            }
            if (Vb6Patterns.GuardInsertionLine().IsMatch(line.TrimStart())) {
                continue;
            }
            return index;
        }
        return -1;
    }

    /// <summary>
    /// Applies line insertions bottom-up so earlier line indexes stay valid
    /// while later ones are inserted. Multiple insertions at the same index
    /// keep their list order (they are applied in reverse). Each inserted
    /// line takes the terminator of the line it precedes (falling back to
    /// the preceding line's, then CRLF) so a missing final newline keeps
    /// sitting on the file's last line.
    /// </summary>
    public static void ApplyInsertions(Vb6FileModel model, IEnumerable<LineInsertion> insertions)
    {
        var contents = new List<string>(model.Contents);
        var terminators = new List<string>(model.Terminators);

        var ordered = insertions
            .Select((insertion, sequence) => (insertion, sequence))
            .OrderByDescending(entry => entry.insertion.LineIndex)
            .ThenByDescending(entry => entry.sequence);

        foreach (var (insertion, _) in ordered) {
            var following = insertion.LineIndex < terminators.Count && insertion.LineIndex >= 0
                ? terminators[insertion.LineIndex]
                : string.Empty;
            if (following.Length == 0) {
                following = insertion.LineIndex > 0 ? terminators[insertion.LineIndex - 1] : "\r\n";
                if (following.Length == 0) {
                    following = "\r\n";
                }
            }
            contents.Insert(insertion.LineIndex, insertion.Content);
            terminators.Insert(insertion.LineIndex, following);
        }

        model.Contents = [.. contents];
        model.Terminators = [.. terminators];
    }

    /// <summary>Leading space/tab count of a line.</summary>
    public static int LeadingWhitespace(string line)
    {
        var leading = 0;
        while (leading < line.Length && (line[leading] == ' ' || line[leading] == '\t')) {
            leading++;
        }
        return leading;
    }

    /// <summary>
    /// Sanitizes a member name for use inside a generated label:
    /// [bracketed] identifiers are unbracketed and every character outside
    /// [A-Za-z0-9_] becomes an underscore.
    /// </summary>
    public static string SanitizeLabelPart(string memberName)
    {
        var sanitized = memberName.Trim('[', ']');
        var builder = new System.Text.StringBuilder(sanitized.Length);
        foreach (var character in sanitized) {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }
        return builder.ToString();
    }
}
