#nullable enable
using System.Text.RegularExpressions;

namespace VB6Instrument;

/// <summary>Pass A outcome: the shared counters plus the captured
/// instrumentation map file entry.</summary>
public sealed class LineNumbersResult : PassResult
{
    public LineNumbersResult() : base("line-numbers") { }

    /// <summary>The file's map entry: member ranges in original-file lines
    /// (pass A never inserts or deletes lines, and the line-inserting
    /// passes B and C only run after it) plus the virtual-to-real line
    /// maps.</summary>
    public MapFile FileMap { get; set; } = new();
}

/// <summary>
/// Pass A — virtual line numbers. Port of the incumbent
/// Set-Vb6LineNumbers.ps1 (Add mode): numbers executable statements inside
/// member bodies starting at 10 in steps of 10, pushes bodies out to the
/// numbering baseline (10 spaces) keeping the code column fixed, keeps the
/// legacy column-1 label form, and leaves already-numbered members
/// untouched. The scheme must byte-match the incumbent tool's output.
/// </summary>
public sealed partial class LineNumbersPass
{
    private readonly string? fileType;

    public LineNumbersPass(string? fileType = null)
    {
        this.fileType = fileType;
    }

    public const int NumberStart = 10;
    public const int NumberStep = 10;
    public const int MaxLineNumber = 65535;

    [GeneratedRegex(@"^Select Case\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SelectCasePattern();

    [GeneratedRegex(@"^Case\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CasePattern();

    [GeneratedRegex(@"^End Select\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EndSelectPattern();

    /// <summary>A guard-handler label statement (the tokenizer strips the
    /// colon) — marks a member already instrumented by the error-guard pass,
    /// which pass A must leave untouched so the inserted handler lines are
    /// never numbered on re-runs.</summary>
    [GeneratedRegex(@"^GuardError_\w+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GuardLabelStatement();

    public LineNumbersResult Run(Vb6FileModel model)
    {
        Vb6FileParser.Refresh(model);

        var lineCount = model.LineCount;
        var contents = model.Contents;

        // Map every physical line to the member (by index) whose body it
        // belongs to (interior lines only), and record the first physical
        // line of every logical statement. Only statement-start lines can
        // carry line numbers; continuation lines never do.
        var bodyOfLine = new int?[lineCount];
        var statementStart = new bool[lineCount];
        for (var memberIndex = 0; memberIndex < model.Members.Count; memberIndex++) {
            var range = model.Members[memberIndex];
            for (var index = range.BodyStartIndex; index <= range.BodyEndIndex; index++) {
                bodyOfLine[index] = memberIndex;
            }
        }
        foreach (var statement in model.Statements) {
            statementStart[statement.StartLine - 1] = true;
        }

        // Detect the first Case clause of every Select Case block: a line
        // number between Select Case and its first Case is a compile error.
        // The convention leaves the first Case unnumbered and numbers the
        // remaining Case clauses.
        var firstCaseLine = new bool[lineCount];
        foreach (var range in model.Members) {
            var awaitingFirstCase = false;
            for (var index = range.BodyStartIndex; index <= range.BodyEndIndex; index++) {
                if (!statementStart[index]) {
                    continue;
                }
                var code = contents[index];
                var numbered = Vb6Patterns.Numbered().Match(code);
                if (numbered.Success) {
                    code = numbered.Groups[3].Value;
                }
                code = code.TrimStart();
                if (!awaitingFirstCase) {
                    if (SelectCasePattern().IsMatch(code)) {
                        awaitingFirstCase = true;
                    }
                    continue;
                }
                if (CasePattern().IsMatch(code)) {
                    firstCaseLine[index] = true;
                    awaitingFirstCase = false;
                }
                else if (EndSelectPattern().IsMatch(code)) {
                    awaitingFirstCase = false;
                }
            }
        }

        // Collect the physical lines that carry at least one executable
        // statement. Bare labels emit a statement too, so label-only lines
        // are filtered again when the numbers are applied.
        var numberableLines = new List<int>[model.Members.Count];
        for (var memberIndex = 0; memberIndex < numberableLines.Length; memberIndex++) {
            numberableLines[memberIndex] = [];
        }
        foreach (var statement in model.Statements) {
            if (Vb6Patterns.NonNumbered().IsMatch(statement.Text)) {
                continue;
            }
            if (firstCaseLine[statement.StartLine - 1]) {
                continue;
            }
            var owner = bodyOfLine[statement.StartLine - 1];
            if (owner is not null) {
                numberableLines[owner.Value].Add(statement.StartLine - 1);
            }
        }

        var result = new LineNumbersResult { MembersSeen = model.Members.Count };
        var linesChanged = 0;

        for (var memberIndex = 0; memberIndex < model.Members.Count; memberIndex++) {
            var range = model.Members[memberIndex];
            var numberable = numberableLines[memberIndex];
            var changedLines = new HashSet<int>();

            // Detect this member's statement baseline. Numbered statements
            // vote with their number+padding field width (that field
            // replaces the pushed indent); plain statements vote with their
            // leading spaces. Labels never vote: they sit in column 1 in
            // every era of the corpus and would drag the baseline to 0.
            var baseline = -1;
            foreach (var lineIndex in numberable) {
                var content = contents[lineIndex];
                if (Vb6Patterns.LabelStart().IsMatch(content)) {
                    continue;
                }
                int leading;
                var numbered = Vb6Patterns.Numbered().Match(content);
                if (numbered.Success) {
                    if (numbered.Groups[2].Length == 1 && Vb6Patterns.LabelStart().IsMatch(numbered.Groups[3].Value)) {
                        continue;
                    }
                    leading = numbered.Groups[1].Length + numbered.Groups[2].Length;
                }
                else {
                    leading = PassHelper.LeadingWhitespace(content);
                }
                if (baseline < 0 || leading < baseline) {
                    baseline = leading;
                }
            }
            if (baseline < 0) {
                baseline = PassHelper.NaturalBodyIndent;
            }

            if (numberable.Count == 0) {
                continue;
            }

            // Members that already carry numbers are left untouched.
            var alreadyNumbered = false;
            foreach (var lineIndex in numberable) {
                if (Vb6Patterns.AnyNumber().IsMatch(contents[lineIndex])) {
                    alreadyNumbered = true;
                    break;
                }
            }
            if (alreadyNumbered) {
                result.MembersSkipped++;
                continue;
            }

            // Members already carrying a tool-inserted error guard are left
            // untouched: their handler lines are statements and would be
            // numbered on a re-run, shifting Erl for inserted code.
            var hasGuardLabel = false;
            foreach (var statement in model.Statements) {
                if (range.IsInRange(statement.StartLine - 1) && GuardLabelStatement().IsMatch(statement.Text)) {
                    hasGuardLabel = true;
                    break;
                }
            }
            if (hasGuardLabel) {
                result.MembersSkipped++;
                continue;
            }

            // Push the whole body out so the statement baseline lands on the
            // numbering baseline (MZ-Tools behaviour). Labels stay put;
            // continuation lines are always pushed (a leading "a:=1" named
            // argument is not a label).
            var push = PassHelper.NumberedBodyIndent - baseline;
            if (push > 0) {
                for (var index = range.BodyStartIndex; index <= range.BodyEndIndex; index++) {
                    var line = contents[index];
                    if (line.Length == 0) {
                        continue;
                    }
                    if (statementStart[index] && Vb6Patterns.LabelStart().IsMatch(line)) {
                        continue;
                    }
                    contents[index] = new string(' ', push) + line;
                    changedLines.Add(index);
                }
            }

            // Number every distinct numberable line. The number plus padding
            // replaces the statement's own (pushed) indentation, so the code
            // column never moves; a column-1 label keeps the legacy
            // one-space form; a statement whose indent is too narrow to
            // hold its number stays pushed but unnumbered (and does not
            // consume a number).
            var nextNumber = NumberStart;
            foreach (var lineIndex in numberable.Distinct().OrderBy(index => index)) {
                var content = contents[lineIndex];
                if (Vb6Patterns.LabelOnly().IsMatch(content)) {
                    continue;
                }
                if (nextNumber > MaxLineNumber) {
                    throw new InstrumentationException(model.FilePath, $"Member '{range.Name}' at line {range.StartIndex + 1} needs a line number above {MaxLineNumber}.");
                }
                var numberText = nextNumber.ToString();
                var leading = PassHelper.LeadingWhitespace(content);
                int pad;
                if (leading == 0 && Vb6Patterns.LabelStart().IsMatch(content)) {
                    pad = 1;
                }
                else {
                    pad = leading - numberText.Length;
                    if (pad < 1) {
                        continue;
                    }
                }
                contents[lineIndex] = numberText + new string(' ', pad) + content[leading..];
                changedLines.Add(lineIndex);
                nextNumber += NumberStep;
            }

            linesChanged += changedLines.Count;
            if (changedLines.Count > 0) {
                result.MembersTouched++;
            }
        }

        result.LinesChanged = linesChanged;

        // Numbers outside member bodies are never touched, only reported.
        var outsideBody = 0;
        for (var index = Math.Max(0, model.CodeStartIndex); index < lineCount; index++) {
            if (bodyOfLine[index] is null && Vb6Patterns.AnyNumber().IsMatch(contents[index])) {
                outsideBody++;
            }
        }
        if (outsideBody > 0) {
            result.Warnings.Add($"{outsideBody} numbered line(s) outside member bodies left untouched");
        }

        result.FileMap = MapFile.Capture(model, fileType);

        return result;
    }
}
