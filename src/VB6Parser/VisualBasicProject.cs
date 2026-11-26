#nullable enable
namespace VB6Parser;

public class VisualBasicProject
{
    public required string Name { get; init; }

    /// <summary>Binary version from the MajorVer/MinorVer/RevisionVersion
    /// keys, or null when the .vbp declares none.</summary>
    public VisualBasicProjectVersion? Version { get; set; }

    /// <summary>Descriptive metadata from the Make tab (Title and the
    /// Version* keys), keyed by friendly name; empty when the .vbp
    /// declares none.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } =
        new Dictionary<string, string>();

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

        var versionMajor = 0;
        var versionMinor = 0;
        var versionRevision = 0;
        var hasVersion = false;
        var metadata = new Dictionary<string, string>();

        string? line;
        while ((line = reader.ReadLine()) != null) {
            var bits = line.Split('=', 2);
            if (bits.Length != 2) {
                continue;
            }

            var key   = bits[0].Trim();
            var value = bits[1].Trim();

            switch (key) {
                case "MajorVer" when int.TryParse(value, out versionMajor):
                case "MinorVer" when int.TryParse(value, out versionMinor):
                case "RevisionVersion" when int.TryParse(value, out versionRevision):
                    hasVersion = true;
                    break;

                case "Title": AddMetadata(metadata, "title", value); break;
                case "VersionComments": AddMetadata(metadata, "comments", value); break;
                case "VersionCompanyName": AddMetadata(metadata, "companyName", value); break;
                case "VersionFileDescription": AddMetadata(metadata, "fileDescription", value); break;
                case "VersionLegalCopyright": AddMetadata(metadata, "legalCopyright", value); break;
                case "VersionLegalTrademarks": AddMetadata(metadata, "legalTrademarks", value); break;
                case "VersionProductName": AddMetadata(metadata, "productName", value); break;

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

        if (hasVersion) {
            project.Version = new VisualBasicProjectVersion(versionMajor, versionMinor, versionRevision);
        }
        if (metadata.Count > 0) {
            project.Metadata = metadata;
        }

        return project;
    }

    /// <summary>Strips one pair of surrounding quotes from a .vbp string value.</summary>
    static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"')
            ? trimmed[1..^1]
            : trimmed;
    }

    /// <summary>Records a Make-tab metadata value; empty (unquoted) values are omitted.</summary>
    static void AddMetadata(Dictionary<string, string> metadata, string key, string value)
    {
        var unquoted = Unquote(value);
        if (unquoted.Length > 0) {
            metadata[key] = unquoted;
        }
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

            return new VisualBasicProjectReference(kind, guid, major, minor, lcid, description, declaredPath);
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

            return new VisualBasicProjectReference(
                ProjectReferenceKind.ActiveX, guid, major, minor, lcid,
                Description: filePart,
                DeclaredPath: filePart);
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
}

