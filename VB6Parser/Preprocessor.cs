using System.Text;
using System.Text.RegularExpressions;

namespace VB6Parser;

internal static partial class Preprocessor
{
    [GeneratedRegex(@"^(.+)\s*('.*)$")]
    private static partial Regex TrailingComments();

    [GeneratedRegex(@"^\s*([\w_]+?\:)\s{2}\s+(.+)$")]
    private static partial Regex LabelWithStatementsOnSameLine();

    [GeneratedRegex(@"\s*[^\w].*?:\s*$")]
    private static partial Regex StatementThatLooksLikeLabel();

    public static string Preprocess(TextReader text)
    {
        var sb = new StringBuilder();

        bool inDesigner = false;
        bool inContinuation = false;

        string continuedFrom = null;

        string line;
        while ((line = text.ReadLine()) != null) {
            if (line.StartsWith("Begin", StringComparison.InvariantCultureIgnoreCase)) {
                inDesigner = true;
            }
            else if (line.StartsWith("End", StringComparison.InvariantCultureIgnoreCase)) {
                inDesigner = false;
            }

            if (!inDesigner) {
                // Remove trailing whitespace
                line = line.TrimEnd();

                // Remove line numbers
                line = line.TrimStart(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);

                // Join continuations
                if (continuedFrom != null) {
                    line = continuedFrom + " " + line.TrimStart();
                    continuedFrom = null;
                }
                if (line.EndsWith('_')) {
                    continuedFrom = line.TrimEnd('_').TrimEnd();
                    continue;
                }

                // Put all comments on own line
                if (SplitComment(line) is (string code, string comment) 
                    && !string.IsNullOrWhiteSpace(code) && !string.IsNullOrEmpty(comment)) {
                    sb.Append(new string([.. code.TakeWhile(c => c == ' ' || c == '\t')]));
                    sb.AppendLine(comment);
                    line = code;
                }

                // Split labels inline with statements
                if (LabelWithStatementsOnSameLine().Match(line) is Match m && m.Success) {
                    sb.AppendLine(m.Groups[1].Value);
                    line = "        " + m.Groups[2].Value.TrimEnd();
                }
            
                if (StatementThatLooksLikeLabel().IsMatch(line) && !line.Contains("Case ")) {
                    line = line.TrimEnd(':');
                }
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    static (string code, string comment) SplitComment(string line)
    {
        if (!line.Contains('\'')) {
            return (line, "");
        }

        List<char> code = [];
        List<char> comment = [];

        bool inString = false;
        bool inComment = false;
        bool inEscape = false;

        foreach (var c in line) {
            if (!inComment) {
                if (!inString) { // in code
                    if (c == '\'') {
                        inComment = true;
                    }
                    else if (c == '"') {
                        inString = true;
                    }
                }
                else { // in string
                    if (!inEscape) {
                        if (c == '"') {
                            inString = false;
                        }
                        else if (c == '\\') {
                            inEscape = true;
                        }
                    }
                    else { // in escape
                        inEscape = false;
                    }
                }
            }

            if (inComment) {
                comment.Add(c);
            }
            else {
                code.Add(c);
            }
        }

        return (new string([.. code]).TrimEnd(), new string([.. comment]).TrimStart());
    }
}