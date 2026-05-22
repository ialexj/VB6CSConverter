#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ComQuery;

// ──────────────────────────────────────────────────────────────────────────
// Enumerations  (kept identical to ComStubGenerator's LibraryTypeKind /
//                LibraryMemberKind so they round-trip cleanly via JSON)
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
// JSON model records
// ──────────────────────────────────────────────────────────────────────────

/// <summary>One formal parameter of a COM method.</summary>
public record ComQueryParam(
    string Name,
    string Type,
    bool IsOptional,
    bool IsOut);

/// <summary>One method, property getter/setter, or struct field on a COM type.</summary>
public record ComQueryMember(
    string Name,
    LibraryMemberKind Kind,
    string ReturnType,
    IReadOnlyList<ComQueryParam> Parameters,
    bool IsDefault = false,
    int? DispId = null,
    string? Description = null);

/// <summary>A named constant in an enum type.</summary>
public record ComQueryEnumVal(string Name, long Value);

/// <summary>One type extracted from a COM type library.</summary>
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

/// <summary>A foreign type library discovered while inspecting a COM type library.</summary>
public record ComQueryDiscoveredDep(Guid Guid, int Major, int Minor);

/// <summary>All type information extracted from a single COM type library.</summary>
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
    string? Description = null);

/// <summary>Identifies a COM type library for inspection by GUID, version, and metadata.</summary>
public record ComReference(
    Guid Guid,
    int MajorVersion,
    int MinorVersion,
    int Lcid,
    string Description,
    bool IsTransitive = false);
