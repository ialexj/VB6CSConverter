#nullable enable
using System;
using System.Collections.Generic;

namespace VB6Converter.ReferenceStubs;

// ──────────────────────────────────────────────────────────────────────────
// Enumerations
// ──────────────────────────────────────────────────────────────────────────

public enum LibraryTypeKind
{
    /// <summary>VB6 Enum or a COM TKIND_ENUM.</summary>
    Enum,
    /// <summary>A COM dispatch interface (IDispatch-derived).</summary>
    DispatchInterface,
    /// <summary>A pure vtable COM interface.</summary>
    Interface,
    /// <summary>A COM coclass (concrete creatable class).</summary>
    Class,
    /// <summary>A COM module (static methods/constants only).</summary>
    Module,
    /// <summary>A COM typedef alias (TKIND_ALIAS), e.g. OLE_HANDLE = uint.</summary>
    Alias,
    /// <summary>A COM record or union (TKIND_RECORD / TKIND_UNION); maps to a C# struct.</summary>
    Struct,
}

public enum LibraryMemberKind
{
    Method,
    PropertyGet,
    PropertySet,
    /// <summary>A named instance field of a COM struct (TKIND_RECORD / TKIND_UNION).</summary>
    Field,
}

// ──────────────────────────────────────────────────────────────────────────
// Symbol model records
// ──────────────────────────────────────────────────────────────────────────

/// <summary>One formal parameter of a COM method.</summary>
public record LibraryParameterModel(
    string Name,
    string CSharpType,
    bool IsOptional,
    bool IsOut);

/// <summary>One method, property getter, or property setter on a COM type.</summary>
public record LibraryMemberModel(
    string Name,
    LibraryMemberKind Kind,
    string ReturnCSharpType,
    IReadOnlyList<LibraryParameterModel> Parameters);

/// <summary>A named constant in an enum type.</summary>
public record LibraryEnumValueModel(string Name, long Value);

/// <summary>
/// One type (enum, interface, class, module) extracted from a COM type library.
/// </summary>
public record LibraryTypeModel(
    string Name,
    LibraryTypeKind Kind,
    IReadOnlyList<LibraryMemberModel> Members,
    IReadOnlyList<LibraryEnumValueModel> EnumValues,
    string? AliasedCSharpType = null,
    IReadOnlyList<string>? ImplementedInterfaces = null);

/// <summary>
/// A foreign type library that was referenced while inspecting a COM type library.
/// Discovered via VT_USERDEFINED → ITypeInfo::GetContainingTypeLib.
/// </summary>
public record DiscoveredDependency(Guid Guid, int Major, int Minor);

/// <summary>All type information extracted from a single COM type library.</summary>
public record LibraryModel(
    string Name,
    string SafeName,
    Guid Guid,
    int Major,
    int Minor,
    IReadOnlyList<LibraryTypeModel> Types,
    IReadOnlyList<DiscoveredDependency> DiscoveredDependencies);
