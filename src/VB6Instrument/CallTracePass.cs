#nullable enable
using System.Text.RegularExpressions;

namespace VB6Instrument;

/// <summary>
/// Pass B — method traces. Prepends a templated statement to every
/// Sub / Function / Property body ({File} = file name without extension,
/// {Member} = member name). The line is inserted at the numbering baseline
/// (10 spaces), unnumbered, so Erl positions from pass A stay valid —
/// hence traces run after line numbers. Members with a ' [NoTrace]
/// comment are skipped; already-inserted traces of the same shape are
/// detected, making the pass idempotent.
/// </summary>
public sealed class CallTracePass
{
    private readonly string _template;
    private readonly Lazy<Regex> _detectionPattern;

    public CallTracePass(string template)
    {
        _template = template;
        _detectionPattern = new Lazy<Regex>(() => BuildDetectionPattern(template));
    }

    /// <summary>
    /// Builds the idempotence detector: the rendered template with the
    /// {File}/{Member} substitutions wildcarded, so a previous run's trace
    /// is recognized even after a file rename (matching the incumbent
    /// tool's lenient any-argument detection).
    /// </summary>
    private static Regex BuildDetectionPattern(string template)
    {
        var parts = Regex.Split(template, @"\{(file|member)\}", RegexOptions.IgnoreCase);
        var builder = new System.Text.StringBuilder("^");
        foreach (var part in parts) {
            var isPlaceholder = part.Equals("file", StringComparison.OrdinalIgnoreCase) || part.Equals("member", StringComparison.OrdinalIgnoreCase);
            builder.Append(isPlaceholder ? ".+?" : Regex.Escape(part));
        }
        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public PassResult Run(Vb6FileModel model)
    {
        Vb6FileParser.Refresh(model);
        var contents = model.Contents;
        var result = new PassResult("call-trace") { MembersSeen = model.Members.Count };
        var insertions = new List<LineInsertion>();

        foreach (var range in model.Members) {
            if (ContainsMarker(contents, range, "NoTrace")) {
                result.MembersSkipped++;
                continue;
            }

            var insertIndex = PassHelper.FindFirstCodeLine(contents, range.FirstInsertableIndex, range.EndIndex);
            if (insertIndex < 0) {
                result.Warnings.Add($"member '{range.Name}' at line {range.StartIndex + 1} has no insertable statement; skipped");
                result.MembersSkipped++;
                continue;
            }

            var rendered = PassHelper.RenderTemplate(_template, model.FileName, range.Name);

            // Idempotence: the first code-bearing statement of an already
            // traced member matches the template shape (placeholders
            // wildcarded), with an optional line number prefix from pass A.
            var probe = contents[insertIndex].TrimStart();
            var numbered = Vb6Patterns.Numbered().Match(probe);
            if (numbered.Success) {
                probe = numbered.Groups[3].Value.TrimStart();
            }
            if (_detectionPattern.Value.IsMatch(probe)) {
                result.MembersSkipped++;
                continue;
            }

            insertions.Add(new LineInsertion(insertIndex, new string(' ', PassHelper.NumberedBodyIndent) + rendered));
        }

        PassHelper.ApplyInsertions(model, insertions);
        result.MembersTouched = insertions.Count;
        result.LinesChanged = insertions.Count;
        return result;
    }

    internal static bool ContainsMarker(string[] contents, Vb6MemberRange range, string markerName)
    {
        var pattern = Regex.Escape($"[{markerName}]");
        for (var index = range.StartIndex; index <= range.EndIndex; index++) {
            if (Regex.IsMatch(contents[index], pattern, RegexOptions.IgnoreCase)) {
                return true;
            }
        }
        return false;
    }
}
