using System;
using System.Text;

namespace ComQuery;

/// <summary>
/// Pure utility methods for normalising COM library and identifier names
/// into safe C# identifiers.
/// </summary>
public static class ReferenceNaming
{
    public static string MakeSafeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "UnknownLib";

        var sb             = new StringBuilder(raw.Length);
        bool capitalizeNext = false;

        foreach (char c in raw.Trim()) {
            if (char.IsLetterOrDigit(c)) {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else {
                capitalizeNext = sb.Length > 0;
            }
        }

        return sb.Length == 0 ? "UnknownLib" : sb.ToString();
    }
}
