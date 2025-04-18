using System.Text;
using System.Text.RegularExpressions;

namespace VB6Parser;

internal static partial class Preprocessor
{
    [GeneratedRegex(@"^([^\s]+)\s('.*)$")]
    private static partial Regex TrailingComments();

    [GeneratedRegex(@"^\s*([\w_]+?\:)\s{2}\s+(.+)$")]
    private static partial Regex LabelWithStatementsOnSameLine();

    [GeneratedRegex(@"\s*[^\w].*?:\s*$")]
    private static partial Regex StatementThatLooksLikeLabel();

    public static string Preprocess(TextReader text)
    {
        var sb = new StringBuilder();

        string line;
        while ((line = text.ReadLine()) != null) {
            // Remove line numbers
            line = line.TrimStart(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);

            // Put all comments on own line
            if (TrailingComments().Match(line) is Match trailingCommentMatch && trailingCommentMatch.Success) {
                sb.AppendLine(trailingCommentMatch.Groups[2].Value);
                line = trailingCommentMatch.Groups[1].Value;
            }

            // Split labels inline with statements
            if (LabelWithStatementsOnSameLine().Match(line) is Match m && m.Success) {
                sb.AppendLine(m.Groups[1].Value);
                line = "        " + m.Groups[2].Value;
            }

            // Remove trailing whitespace
            line = line.TrimEnd();
            
            if (StatementThatLooksLikeLabel().IsMatch(line) && !line.Contains("Case ")) {
                line = line.TrimEnd(':');
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }
}