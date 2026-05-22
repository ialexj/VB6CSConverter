#nullable enable
using System.Collections.Generic;

namespace ComStubGenerator;

/// <summary>
/// Represents one entry in the synthetic members JSON file.
/// Each entry specifies a set of targets (<c>LibraryName.TypeName</c>) and the members
/// to inject into each matched type.
/// </summary>
public record SyntheticMemberSet(
    IReadOnlyList<string> Targets,
    IReadOnlyList<ComQueryMember> Members);
