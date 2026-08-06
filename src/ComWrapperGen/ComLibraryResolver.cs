using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ComWrapperGen;

public sealed record ResolvedComLibrary(string Name, Guid Guid, int Major, int Minor, string Path);

public static class ComLibraryResolver
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int LoadTypeLib([MarshalAs(UnmanagedType.LPWStr)] string szFile,
                                  [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.ITypeLib ppTLib);

    [StructLayout(LayoutKind.Sequential)]
    struct TLIBATTR
    {
        public Guid guid;
        public int lcid;
        public int syskind;
        public short wMajorVerNum;
        public short wMinorVerNum;
        public short wLibFlags;
    }

    public static ResolvedComLibrary Resolve(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) {
            throw new ArgumentException("--lib cannot be empty.");
        }

        if (File.Exists(spec)) {
            return ReadIdentityFromTypeLibPath(spec);
        }

        bool hasVersion = TryParseVersionSuffix(spec, out string core, out int major, out int minor);

        if (TryParseGuid(core, out Guid guid)) {
            string? path = TypeLibraryInspector.ResolveTypeLibPath(guid, hasVersion ? major : 0, hasVersion ? minor : 0);
            if (path is null) {
                throw new InvalidOperationException($"No registered type library found for GUID {guid:B}.");
            }

            var resolved = ReadIdentityFromTypeLibPath(path);
            if (hasVersion && (resolved.Major != major || resolved.Minor != minor)) {
                throw new InvalidOperationException(
                    $"GUID {guid:B} was resolved, but requested version {major}.{minor} was not found. Resolved {resolved.Major}.{resolved.Minor} at '{resolved.Path}'.");
            }

            return resolved;
        }

        var candidates = EnumerateRegisteredLibraries()
            .Where(x => hasVersion ? x.Major == major && x.Minor == minor : true)
            .ToList();

        var exact = candidates
            .Where(x => string.Equals(x.Name, core, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exact.Count == 1) {
            return exact[0];
        }

        if (exact.Count > 1) {
            throw BuildAmbiguousNameError(core, exact);
        }

        var fuzzy = candidates
            .Where(x => x.Name.Contains(core, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (fuzzy.Count == 1) {
            return fuzzy[0];
        }

        if (fuzzy.Count == 0) {
            throw new InvalidOperationException($"No registered type library matched '{spec}'.");
        }

        throw BuildAmbiguousNameError(core, fuzzy);
    }

    static Exception BuildAmbiguousNameError(string name, IReadOnlyList<ResolvedComLibrary> matches)
    {
        string lines = string.Join(Environment.NewLine, matches
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"  - {x.Name} ({x.Guid:B} {x.Major}.{x.Minor}) => {x.Path}"));

        return new InvalidOperationException(
            $"Library name '{name}' is ambiguous. Use a GUID or path. Candidates:{Environment.NewLine}{lines}");
    }

    static bool TryParseVersionSuffix(string spec, out string core, out int major, out int minor)
    {
        core = spec;
        major = 0;
        minor = 0;

        string[] parts = spec.Split(',');
        if (parts.Length < 3) {
            return false;
        }

        if (!int.TryParse(parts[^2], out major) || !int.TryParse(parts[^1], out minor)) {
            return false;
        }

        core = string.Join(',', parts[..^2]).Trim();
        return true;
    }

    static bool TryParseGuid(string text, out Guid guid)
    {
        string trimmed = text.Trim();
        return Guid.TryParse(trimmed, out guid)
            || Guid.TryParse(trimmed.Trim('{', '}'), out guid);
    }

    static IReadOnlyList<ResolvedComLibrary> EnumerateRegisteredLibraries()
    {
        var list = new List<ResolvedComLibrary>();
        using var typeLibRoot = Registry.ClassesRoot.OpenSubKey("TypeLib");
        if (typeLibRoot is null) {
            return list;
        }

        foreach (string guidKeyName in typeLibRoot.GetSubKeyNames()) {
            if (!Guid.TryParseExact(guidKeyName, "B", out Guid libid)) {
                continue;
            }

            using var guidKey = typeLibRoot.OpenSubKey(guidKeyName);
            if (guidKey is null) {
                continue;
            }

            foreach (string versionKeyName in guidKey.GetSubKeyNames()) {
                if (!TryParseVersion(versionKeyName, out int major, out int minor)) {
                    continue;
                }

                using var versionKey = guidKey.OpenSubKey(versionKeyName);
                if (versionKey is null) {
                    continue;
                }

                string name = versionKey.GetValue(null) as string ?? guidKeyName;
                string? path = FindPathForCurrentArch(versionKey);
                if (string.IsNullOrWhiteSpace(path)) {
                    continue;
                }

                list.Add(new ResolvedComLibrary(name, libid, major, minor, path));
            }
        }

        return list;
    }

    static bool TryParseVersion(string versionKeyName, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        string[] parts = versionKeyName.Split('.');
        if (parts.Length != 2) {
            return false;
        }

        return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
    }

    static string? FindPathForCurrentArch(RegistryKey versionKey)
    {
        foreach (string lcid in OrderPrefer(versionKey.GetSubKeyNames(), "0")) {
            using var lcidKey = versionKey.OpenSubKey(lcid);
            if (lcidKey is null) {
                continue;
            }

            string preferred = Environment.Is64BitProcess ? "win64" : "win32";
            string fallback = Environment.Is64BitProcess ? "win32" : "win64";
            foreach (string arch in OrderPrefer(lcidKey.GetSubKeyNames(), preferred, fallback)) {
                using var archKey = lcidKey.OpenSubKey(arch);
                string? path = archKey?.GetValue(null) as string;
                if (TypeLibraryInspector.IsTypeLibPath(path)) {
                    return path;
                }
            }
        }

        return null;
    }

    static IEnumerable<string> OrderPrefer(string[] keys, params string[] preferred)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in preferred) {
            foreach (string k in keys) {
                if (string.Equals(p, k, StringComparison.OrdinalIgnoreCase) && seen.Add(k)) {
                    yield return k;
                }
            }
        }

        foreach (string key in keys) {
            if (seen.Add(key)) {
                yield return key;
            }
        }
    }

    static ResolvedComLibrary ReadIdentityFromTypeLibPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        int hr = LoadTypeLib(fullPath, out var typeLib);
        if (hr != 0 || typeLib is null) {
            throw new InvalidOperationException($"LoadTypeLib failed for '{fullPath}' with HRESULT 0x{hr:X8}.");
        }

        try {
            typeLib.GetLibAttr(out IntPtr pLibAttr);
            if (pLibAttr == IntPtr.Zero) {
                throw new InvalidOperationException($"Unable to read LIBATTR from '{fullPath}'.");
            }

            try {
                var libAttr = Marshal.PtrToStructure<TLIBATTR>(pLibAttr);
                typeLib.GetDocumentation(-1, out string name, out _, out _, out _);
                if (string.IsNullOrWhiteSpace(name)) {
                    name = Path.GetFileNameWithoutExtension(fullPath);
                }

                return new ResolvedComLibrary(name, libAttr.guid, libAttr.wMajorVerNum, libAttr.wMinorVerNum, fullPath);
            }
            finally {
                typeLib.ReleaseTLibAttr(pLibAttr);
            }
        }
        finally {
            Marshal.ReleaseComObject(typeLib);
        }
    }
}
