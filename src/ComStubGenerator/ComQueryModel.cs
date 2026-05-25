#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ComStubGenerator;

// ──────────────────────────────────────────────────────────────────────────
// Enumerations (identical values to ComQuery project for JSON round-tripping)
// ──────────────────────────────────────────────────────────────────────────

public enum LibraryTypeKind
{
    Enum,
    DispatchInterface,
    Interface,
    Class,
    Module,
    Alias,
    Struct,
}

public enum LibraryMemberKind
{
    Method,
    PropertyGet,
    PropertySet,
    Field,
}

// ──────────────────────────────────────────────────────────────────────────
// Deserialization model (mirrors ComQuery's output schema)
// ──────────────────────────────────────────────────────────────────────────

public record ComQueryParam(
    string Name,
    string Type,
    bool IsOptional,
    bool IsOut,
    bool IsParamArray = false);

public record ComQueryMember(
    string Name,
    LibraryMemberKind Kind,
    string ReturnType,
    IReadOnlyList<ComQueryParam> Parameters,
    bool IsDefault = false,
    int? DispId = null,
    string? Description = null);

public record ComQueryEnumVal(string Name, long Value);

public record ComQueryType(
    string Name,
    LibraryTypeKind Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ComQueryMember>? Members = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ComQueryEnumVal>? EnumValues = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AliasedType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? ImplementedInterfaces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Description = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool IsControl = false);

public record ComQueryDiscoveredDep(Guid Guid, int Major, int Minor);

public record ComQueryLibrary(
    string Name,
    Guid Guid,
    int Major,
    int Minor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Path = null,
    bool IsTransitive = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ComQueryType>? Types = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ComQueryDiscoveredDep>? DiscoveredDependencies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Description = null)
{
    public string SafeName => ReferenceNaming.MakeSafeName(Name);
}
