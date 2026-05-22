#nullable enable
namespace VB6Parser;

/// <summary>Identifies whether a VBP reference came from a Reference= or Object= line.</summary>
public enum ProjectReferenceKind
{
    /// <summary>A type-library reference declared with <c>Reference=</c>.</summary>
    TypeLibrary,

    /// <summary>An ActiveX/OCX control reference declared with <c>Object=</c>.</summary>
    ActiveX,
}

/// <summary>
/// Represents one reference entry parsed from a VB6 <c>.vbp</c> project file.
/// </summary>
/// <param name="Kind">Whether this came from a <c>Reference=</c> or <c>Object=</c> line.</param>
/// <param name="Guid">The registered GUID of the type library.</param>
/// <param name="MajorVersion">Major version number.</param>
/// <param name="MinorVersion">Minor version number.</param>
/// <param name="Lcid">Locale ID (usually 0).</param>
/// <param name="Description">Human-readable name declared in the VBP line.</param>
/// <param name="DeclaredPath">Raw path string from the VBP line (may be relative, may be empty).</param>
/// <param name="ResolvedPath">
/// Absolute path after resolution, or <see langword="null"/> if the file could not be found.
/// </param>
/// <param name="IsTransitive">Indicates whether this reference comes as a dependency from another reference.</param>
public record class VisualBasicProjectReference(
    ProjectReferenceKind Kind,
    Guid Guid,
    int MajorVersion,
    int MinorVersion,
    int Lcid,
    string Description,
    string DeclaredPath);
