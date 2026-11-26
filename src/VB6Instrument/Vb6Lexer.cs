#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace VB6Instrument;

/// <summary>A logical VB6 statement: comment-stripped, continuation-folded, colon-split.</summary>
/// <param name="Text">Verbatim statement text (strings preserved), trimmed.</param>
/// <param name="MaskedText">Statement text with string literal contents removed —
/// used for code detection that must ignore string literals (e.g. existing
/// <c>On Error</c> detection).</param>
/// <param name="StartLine">1-based first physical line.</param>
/// <param name="EndLine">1-based last physical line.</param>
public sealed record Vb6Statement(string Text, string MaskedText, int StartLine, int EndLine);

public static partial class Vb6Lexer
{
    private static readonly Regex DirectiveLinePattern = new(@"^\s*#(?:If|ElseIf|Else|End|Const)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContinuationPattern = new(@"\s_$", RegexOptions.Compiled);
    private static readonly Regex RemPattern = new(@"^Rem(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Splits VB6 source into logical statements, honouring strings, date
    /// literals, comments, colons and line continuations. Port of the
    /// incumbent Get-Vb6Statement tokenizer (Set-Vb6LineNumbers.ps1), with a
    /// parallel masked-text accumulation for string-insensitive detection.
    /// </summary>
    /// <param name="lines">0-based physical lines.</param>
    /// <param name="firstLine">1-based first line to scan.</param>
    /// <param name="filePath">File path for error reporting.</param>
    public static List<Vb6Statement> Tokenize(string[] lines, int firstLine, string? filePath = null)
    {
        var statements = new List<Vb6Statement>();
        var text = new StringBuilder();
        var masked = new StringBuilder();
        var startLine = firstLine;
        var continued = false;

        for (var index = firstLine - 1; index < lines.Length; index++) {
            var line = lines[index];
            if (!continued && DirectiveLinePattern.IsMatch(line)) {
                continue;
            }
            if (!continued) {
                startLine = index + 1;
            }

            var inString = false;
            var inDate = false;
            for (var column = 0; column < line.Length; column++) {
                var character = line[column];
                if (character == '#' && !inString &&
                    (inDate || column == 0 || !IsWordDotBracket(line[column - 1]))) {
                    inDate = !inDate;
                }
                if (character == '"' && !inDate) {
                    if (inString && column + 1 < line.Length && line[column + 1] == '"') {
                        text.Append("\"\"");
                        masked.Append(' ', 2);
                        column++;
                        continue;
                    }
                    inString = !inString;
                    // Quote delimiters stay in the verbatim text (statement
                    // texts are compared byte-wise elsewhere) but are not
                    // part of the masked text.
                    text.Append(character);
                    continue;
                }
                if (!inString && !inDate) {
                    if (character == '\'') {
                        break;
                    }
                    if ((character is 'r' or 'R') &&
                        string.IsNullOrWhiteSpace(text.ToString()) &&
                        RemPattern.IsMatch(line[column..])) {
                        break;
                    }
                    if (character == ':' &&
                        !(column + 1 < line.Length && line[column + 1] == '=')) {
                        if (text.ToString().Trim().Length > 0) {
                            Emit(statements, text, masked, startLine, index + 1);
                        }
                        text.Clear();
                        masked.Clear();
                        startLine = index + 1;
                        continue;
                    }
                }
                text.Append(character);
                if (!inString && !inDate) {
                    masked.Append(character);
                }
            }

            var code = text.ToString().TrimEnd();
            var maskedCode = masked.ToString().TrimEnd();
            continued = !inString && !inDate && ContinuationPattern.IsMatch(code);
            if (continued) {
                text.Clear();
                text.Append(code[..^1]);
                masked.Clear();
                masked.Append(maskedCode.EndsWith('_') ? maskedCode[..^1] : maskedCode);
            }
            else {
                if (code.Trim().Length > 0) {
                    Emit(statements, text, masked, startLine, index + 1);
                }
                text.Clear();
                masked.Clear();
            }
        }

        if (continued) {
            throw new InstrumentationException(filePath, $"Unfinished line continuation at line {startLine}.");
        }
        return statements;
    }

    private static void Emit(List<Vb6Statement> statements, StringBuilder text, StringBuilder masked, int startLine, int endLine)
    {
        var statementText = text.ToString().Trim();
        var maskedText = masked.ToString().Trim();
        statements.Add(new Vb6Statement(statementText, maskedText, startLine, endLine));
        text.Clear();
        masked.Clear();
    }

    private static bool IsWordDotBracket(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ']';

    /// <summary>
    /// Returns the line with any trailing comment removed: the text before
    /// the first apostrophe that sits outside a double-quoted string. Doubled
    /// quotes inside strings are honoured. VB6 date literals never contain an
    /// apostrophe, so string tracking alone is exact for this purpose.
    /// </summary>
    public static string GetCodePortion(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length; index++) {
            var character = line[index];
            if (inString) {
                if (character == '"') {
                    if (index + 1 < line.Length && line[index + 1] == '"') {
                        index++;
                        continue;
                    }
                    inString = false;
                }
                continue;
            }
            if (character == '"') {
                inString = true;
                continue;
            }
            if (character == '\'') {
                return line[..index].TrimEnd();
            }
        }
        return line;
    }
}
