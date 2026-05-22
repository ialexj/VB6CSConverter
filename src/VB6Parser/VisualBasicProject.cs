#nullable enable
namespace VB6Parser;

public class VisualBasicProject
{
    static readonly Guid ImplicitVb6RuntimeGuid = new("000204EF-0000-0000-C000-000000000046");
    const int ImplicitVb6RuntimeMajor = 6;
    const int ImplicitVb6RuntimeMinor = 0;
    const int ImplicitVb6RuntimeLcid = 9;
    const string ImplicitVb6RuntimeDescription = "Visual Basic For Applications";
    const string ImplicitVb6RuntimeDeclaredPath = "MSVBVM60.DLL";

    static readonly Guid ImplicitStdOleGuid = new("00020430-0000-0000-C000-000000000046");
    const int ImplicitStdOleMajor = 2;
    const int ImplicitStdOleMinor = 0;
    const int ImplicitStdOleLcid = 0;
    const string ImplicitStdOleDescription = "OLE Automation";
    const string ImplicitStdOleDeclaredPath = "stdole2.tlb";

    public string Name { get; set; }

    public List<VisualBasicProjectFile> Files { get; set; } = [];

    public List<VisualBasicProjectReference> References { get; set; } = [];

    public static VisualBasicProject Load(string path)
    {
        const string FormMarker = "Form";
        const string ModuleMarker = "Module";
        const string ClassMarker = "Class";
        const string UserControlMarker = "UserControl";
        const string ReferenceMarker = "Reference";
        const string ObjectMarker = "Object";

        using var reader = new StreamReader(path, VisualBasic6Encoding.Encoding);

        var basePath = Path.GetDirectoryName(path) ?? string.Empty;
        string ResolveFullPath(string relOrAbs)
        {
            relOrAbs = relOrAbs.Trim();
            return Path.IsPathRooted(relOrAbs)
                ? relOrAbs
                : Path.GetFullPath(Path.Combine(basePath, relOrAbs));
        }

        var project = new VisualBasicProject {
            Name = Path.GetFileNameWithoutExtension(path)
        };

        string line;
        while ((line = reader.ReadLine()) != null) {
            var bits = line.Split('=', 2);
            if (bits.Length != 2) {
                continue;
            }

            var key   = bits[0].Trim();
            var value = bits[1].Trim();

            switch (key) {
                case FormMarker:
                case ModuleMarker:
                case ClassMarker:
                case UserControlMarker:
                {
                    VisualBasicFileType fileType = key switch {
                        FormMarker        => VisualBasicFileType.Form,
                        ModuleMarker      => VisualBasicFileType.Module,
                        ClassMarker       => VisualBasicFileType.Class,
                        UserControlMarker => VisualBasicFileType.Control,
                        _                 => VisualBasicFileType.Module,
                    };

                    string name, filePath;
                    var bits2 = value.Split(";", 2);
                    if (bits2.Length == 2) {
                        name     = bits2[0].Trim();
                        filePath = ResolveFullPath(bits2[1]);
                    }
                    else {
                        name     = Path.GetFileNameWithoutExtension(bits2[0]);
                        filePath = ResolveFullPath(bits2[0]);
                    }

                    project.Files.Add(new VisualBasicProjectFile(filePath, name, fileType));
                    break;
                }

                case ReferenceMarker:
                {
                    var refEntry = ParseReferenceEntry(value, basePath, ProjectReferenceKind.TypeLibrary);
                    if (refEntry != null) {
                        project.References.Add(refEntry);
                    }
                    break;
                }

                case ObjectMarker:
                {
                    var refEntry = ParseObjectEntry(value, basePath);
                    if (refEntry != null) {
                        project.References.Add(refEntry);
                    }
                    break;
                }
            }
        }

        AddImplicitVb6RuntimeReference(project, basePath);
        AddImplicitStdOleReference(project, basePath);

        return project;
    }

    static void AddImplicitVb6RuntimeReference(VisualBasicProject project, string basePath)
    {
        bool exists = project.References.Any(r =>
            r.Guid == ImplicitVb6RuntimeGuid
            && r.MajorVersion == ImplicitVb6RuntimeMajor
            && r.MinorVersion == ImplicitVb6RuntimeMinor);

        if (exists) {
            return;
        }

        string? resolvedPath = ResolveReferencePath(
            ImplicitVb6RuntimeDeclaredPath,
            basePath,
            ImplicitVb6RuntimeGuid,
            ImplicitVb6RuntimeMajor,
            ImplicitVb6RuntimeMinor,
            ImplicitVb6RuntimeLcid);

        project.References.Add(new VisualBasicProjectReference(
            ProjectReferenceKind.TypeLibrary,
            ImplicitVb6RuntimeGuid,
            ImplicitVb6RuntimeMajor,
            ImplicitVb6RuntimeMinor,
            ImplicitVb6RuntimeLcid,
            ImplicitVb6RuntimeDescription,
            ImplicitVb6RuntimeDeclaredPath,
            resolvedPath));
    }

    static void AddImplicitStdOleReference(VisualBasicProject project, string basePath)
    {
        bool exists = project.References.Any(r =>
            r.Guid == ImplicitStdOleGuid
            && r.MajorVersion == ImplicitStdOleMajor
            && r.MinorVersion == ImplicitStdOleMinor);

        if (exists) {
            return;
        }

        string? resolvedPath = ResolveReferencePath(
            ImplicitStdOleDeclaredPath,
            basePath,
            ImplicitStdOleGuid,
            ImplicitStdOleMajor,
            ImplicitStdOleMinor,
            ImplicitStdOleLcid);

        project.References.Add(new VisualBasicProjectReference(
            ProjectReferenceKind.TypeLibrary,
            ImplicitStdOleGuid,
            ImplicitStdOleMajor,
            ImplicitStdOleMinor,
            ImplicitStdOleLcid,
            ImplicitStdOleDescription,
            ImplicitStdOleDeclaredPath,
            resolvedPath));
    }

    /// <summary>
    /// Parses a <c>Reference=*\G{GUID}#major.minor#lcid#path#description</c> line.
    /// Returns <see langword="null"/> and logs nothing if the line is malformed.
    /// </summary>
    static VisualBasicProjectReference? ParseReferenceEntry(string value, string basePath, ProjectReferenceKind kind)
    {
        try {
            // Strip the leading *\G prefix if present
            if (value.StartsWith("*\\G", StringComparison.OrdinalIgnoreCase)) {
                value = value[3..];
            }

            var parts = value.Split('#');
            if (parts.Length < 3) {
                return null;
            }

            if (!TryParseGuid(parts[0], out var guid)) {
                return null;
            }

            TryParseVersion(parts[1], out int major, out int minor);
            int.TryParse(parts[2].Trim(), out int lcid);

            string declaredPath  = parts.Length > 3 ? parts[3].Trim() : string.Empty;
            string description   = parts.Length > 4 ? parts[4].Trim() : string.Empty;
            string? resolvedPath = ResolveReferencePath(declaredPath, basePath, guid, major, minor, lcid);

            return new VisualBasicProjectReference(kind, guid, major, minor, lcid, description, declaredPath, resolvedPath);
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Parses an <c>Object={GUID}#major.minor#lcid; filename.ocx</c> line.
    /// </summary>
    static VisualBasicProjectReference? ParseObjectEntry(string value, string basePath)
    {
        try {
            // Split off the component filename after the semicolon
            var semicolonIndex = value.IndexOf(';');
            string metaPart    = semicolonIndex >= 0 ? value[..semicolonIndex] : value;
            string filePart    = semicolonIndex >= 0 ? value[(semicolonIndex + 1)..].Trim() : string.Empty;

            var parts = metaPart.Split('#');
            if (parts.Length < 2) {
                return null;
            }

            if (!TryParseGuid(parts[0], out var guid)) {
                return null;
            }

            TryParseVersion(parts[1], out int major, out int minor);
            int lcid = 0;
            if (parts.Length > 2) {
                int.TryParse(parts[2].Trim(), out lcid);
            }

            string? resolvedPath = ResolveReferencePath(filePart, basePath, guid, major, minor, lcid);

            return new VisualBasicProjectReference(
                ProjectReferenceKind.ActiveX, guid, major, minor, lcid,
                Description: filePart,
                DeclaredPath: filePart,
                ResolvedPath: resolvedPath);
        }
        catch {
            return null;
        }
    }

    static bool TryParseGuid(string raw, out Guid guid)
    {
        raw = raw.Trim().Trim('{', '}');
        return Guid.TryParse(raw, out guid);
    }

    static bool TryParseVersion(string raw, out int major, out int minor)
    {
        major = 0; minor = 0;
        var vp = raw.Trim().Split('.', 2);
        bool ok = int.TryParse(vp[0], out major);
        if (vp.Length > 1) {
            ok &= int.TryParse(vp[1], out minor);
        }
        return ok;
    }

    /// <summary>
    /// Tries to find the physical file for a reference.
    /// First tries the declared path (absolute or relative), then falls back to a
    /// Windows registry lookup for the COM type-library registration.
    /// </summary>
    static string? ResolveReferencePath(string declaredPath, string basePath, Guid guid, int major, int minor, int lcid)
    {
        if (!string.IsNullOrEmpty(declaredPath)) {
            string candidate = Path.IsPathRooted(declaredPath)
                ? declaredPath
                : Path.GetFullPath(Path.Combine(basePath, declaredPath));

            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        // Registry fallback — Windows only
        if (OperatingSystem.IsWindows()) {
            return TryRegistryLookup(guid, major, minor, lcid);
        }

        return null;
    }

    /// <summary>
    /// Resolves a type library GUID + version to its file path via the Windows registry.
    /// Returns <see langword="null"/> when the library is not registered.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string? ResolveTypeLibPath(Guid guid, int major, int minor)
        => TryRegistryLookup(guid, major, minor, 0);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? TryRegistryLookup(Guid guid, int major, int minor, int lcid)
    {
        // HKCR\TypeLib\{GUID}\major.minor\lcid\win32 (or win64)
        //
        // WOW64 redirects Registry.ClassesRoot to different hives depending on process bitness:
        //   32-bit process → HKLM\Software\WOW6432Node\Classes\TypeLib
        //   64-bit process → HKLM\Software\Classes\TypeLib
        // Some type libraries (e.g. MSVBVM60) are registered only in the 64-bit hive even though
        // the DLL itself is 32-bit, so we must try both views explicitly.
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 }) {
            var result = TryRegistryLookupInView(guid, major, minor, lcid, view);
            if (result != null) return result;
        }

        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? TryRegistryLookupInView(Guid guid, int major, int minor, int lcid, Microsoft.Win32.RegistryView view)
    {
        string guidKey = $@"Software\Classes\TypeLib\{{{guid}}}\{major}.{minor}";
        string lcidStr = lcid.ToString();

        using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
        using var typeLibKey = hklm.OpenSubKey(guidKey);
        if (typeLibKey == null) return null;

        // Iterate all LCID subkeys, preferring the requested LCID then 0, then any other.
        // Non-numeric subkeys (FLAGS, HELPDIR) are skipped.
        var lcidOrder = typeLibKey.GetSubKeyNames()
            .Where(k => k.All(char.IsAsciiDigit))
            .OrderBy(k => k == lcidStr ? 0 : k == "0" ? 1 : 2);

        foreach (string lcidSubKey in lcidOrder) {
            using var lcidKey = typeLibKey.OpenSubKey(lcidSubKey);
            if (lcidKey == null) continue;

            foreach (string archKey in new[] { "win64", "win32" }) {
                using var archSubKey = lcidKey.OpenSubKey(archKey);
                var path = archSubKey?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && IsTypeLibPath(path)) {
                    return path;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> refers to a COM type library
    /// that can be loaded — either a plain file path, or an embedded-resource path of the form
    /// <c>file.dll\N</c> where N is a decimal resource identifier understood by
    /// <c>LoadTypeLib</c>.
    /// </summary>
    public static bool IsTypeLibPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (File.Exists(path)) return true;

        // COM type libraries embedded inside DLLs are registered with a trailing resource-ID
        // suffix, e.g. "C:\Windows\System32\MSVBVM60.DLL\2".  File.Exists rejects such paths
        // (the \N component is not a directory), but LoadTypeLib handles them natively.
        int lastSep = path.LastIndexOf('\\');
        if (lastSep > 0)
        {
            string suffix = path[(lastSep + 1)..];
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit))
                return File.Exists(path[..lastSep]);
        }

        return false;
    }
}

