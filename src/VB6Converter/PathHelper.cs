using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VB6Converter;

public static class PathHelper
{
    /// <summary>
    /// Returns the deepest common ancestor directory of all supplied paths plus
    /// <paramref name="projectBasePath"/>.  Falls back to <paramref name="projectBasePath"/>
    /// when paths span multiple drive roots (Windows) or share no common prefix.
    /// </summary>
    public static string GetCommonAncestor(string projectBasePath, IEnumerable<string> filePaths)
    {
        var allDirs = filePaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? Path.GetFullPath(p))
            .Prepend(projectBasePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allDirs.Length == 1)
            return allDirs[0];

        var segments = allDirs
            .Select(d => d.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                                  StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var first = segments[0];
        var commonCount = first.Length;

        foreach (var seg in segments.Skip(1)) {
            var i = 0;
            while (i < commonCount && i < seg.Length &&
                   string.Equals(first[i], seg[i], StringComparison.OrdinalIgnoreCase))
                i++;
            commonCount = i;
        }

        if (commonCount == 0)
            return projectBasePath; // paths span different drives — fall back

        var ancestor = string.Join(Path.DirectorySeparatorChar, first.Take(commonCount));

        // Restore root separator for bare drive letters ("C:" → "C:\")
        if (ancestor.EndsWith(':'))
            ancestor += Path.DirectorySeparatorChar;

        return ancestor;
    }
}
