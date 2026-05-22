#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ComQuery;

/// <summary>
/// Enumerates all COM type libraries registered in the current-process architecture's
/// registry view (x86 process → WOW6432Node hive, x64 process → native hive).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RegistryEnumerator
{
    /// <summary>
    /// Returns a shallow <see cref="ComQueryLibrary"/> for every type library registered
    /// under HKLM\Software\Classes\TypeLib in the current process's registry view.
    /// Types are NOT populated (shallow list mode).
    /// </summary>
    public static IEnumerable<ComQueryLibrary> EnumerateRegisteredLibraries()
    {
        // Registry.ClassesRoot is automatically redirected by WOW64:
        //   32-bit process → HKLM\Software\WOW6432Node\Classes
        //   64-bit process → HKLM\Software\Classes
        using var typeLibRoot = Registry.ClassesRoot.OpenSubKey("TypeLib");
        if (typeLibRoot == null) yield break;

        foreach (var guidKeyName in typeLibRoot.GetSubKeyNames()) {
            if (!Guid.TryParseExact(guidKeyName, "B", out var guid)) continue;

            using var guidKey = typeLibRoot.OpenSubKey(guidKeyName);
            if (guidKey == null) continue;

            foreach (var versionKeyName in guidKey.GetSubKeyNames()) {
                if (!TryParseVersion(versionKeyName, out int major, out int minor)) continue;

                using var versionKey = guidKey.OpenSubKey(versionKeyName);
                if (versionKey == null) continue;

                // Library name is stored under the version key's default value
                string libName = versionKey.GetValue(null) as string ?? guidKeyName;

                // Find the path for the current process architecture
                string? path = FindPathForCurrentArch(versionKey);

                yield return new ComQueryLibrary(
                    Name: libName,
                    Guid: guid,
                    Major: major,
                    Minor: minor,
                    Path: path,
                    IsTransitive: false,
                    Types: null);
            }
        }
    }

    static string? FindPathForCurrentArch(RegistryKey versionKey)
    {
        // Walk lcid sub-keys; prefer 0
        foreach (var lcidKeyName in OrderPrefer(versionKey.GetSubKeyNames(), "0")) {
            using var lcidKey = versionKey.OpenSubKey(lcidKeyName);
            if (lcidKey == null) continue;

            // Prefer win64 on 64-bit process, win32 on 32-bit process
            string preferred = Environment.Is64BitProcess ? "win64" : "win32";
            string fallback  = Environment.Is64BitProcess ? "win32" : "win64";

            foreach (var arch in OrderPrefer(lcidKey.GetSubKeyNames(), preferred, fallback)) {
                using var archKey = lcidKey.OpenSubKey(arch);
                var path = archKey?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
        }

        return null;
    }

    static bool TryParseVersion(string versionKeyName, out int major, out int minor)
    {
        major = minor = 0;
        var parts = versionKeyName.Split('.');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
    }

    static IEnumerable<string> OrderPrefer(string[] keys, params string[] preferred)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in preferred) {
            foreach (var k in keys) {
                if (string.Equals(k, p, StringComparison.OrdinalIgnoreCase) && seen.Add(k))
                    yield return k;
            }
        }
        foreach (var k in keys) {
            if (seen.Add(k))
                yield return k;
        }
    }
}
