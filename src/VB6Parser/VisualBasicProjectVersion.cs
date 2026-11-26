#nullable enable
namespace VB6Parser;

/// <summary>
/// The binary version number of a VB6 project as declared in the .vbp
/// (<c>MajorVer</c>, <c>MinorVer</c>, <c>RevisionVersion</c> keys).
/// </summary>
public sealed record VisualBasicProjectVersion(int Major, int Minor, int Revision);
