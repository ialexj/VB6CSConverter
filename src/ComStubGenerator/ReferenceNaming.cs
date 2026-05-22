using System;
using System.Text;

namespace ComStubGenerator;

/// <summary>
/// Pure utility methods for normalising COM library and identifier names
/// into safe C# identifiers.  No COM interop dependencies.
/// </summary>
public static class ReferenceNaming
{
    /// <summary>
    /// Converts a raw type-library name (which may contain spaces, dots, or
    /// other separators) into a PascalCase identifier that is safe to use as a
    /// C# namespace or directory name.
    /// </summary>
    public static string MakeSafeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "UnknownLib";

        var sb             = new StringBuilder(raw.Length);
        bool capitaliseNext = false;

        foreach (char c in raw.Trim()) {
            if (char.IsLetterOrDigit(c)) {
                sb.Append(capitaliseNext ? char.ToUpperInvariant(c) : c);
                capitaliseNext = false;
            }
            else {
                capitaliseNext = sb.Length > 0; // capitalise after separator
            }
        }

        return sb.Length == 0 ? "UnknownLib" : sb.ToString();
    }
}
