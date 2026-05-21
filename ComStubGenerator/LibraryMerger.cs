#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ComStubGenerator;

/// <summary>
/// Merges <see cref="ComQueryLibrary"/> collections from the x86 and x64 ComQuery
/// invocations into a single unified collection.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>Library merge</b>: union by GUID — both arch-only libraries are included.</item>
///   <item><b>Type merge</b>: union by name within each library.</item>
///   <item>
///     <b>Member merge</b>: union by (Name, Kind).  When both arches supply the same
///     member, the one with the more specific return type is kept:
///     <c>object</c> is least specific; any other concrete type wins.  When both are
///     non-<c>object</c> and differ, the x64 version is preferred (consistent tiebreaker).
///   </item>
/// </list>
/// </remarks>
public static class LibraryMerger
{
    /// <summary>
    /// Merges two sets of libraries (typically from x86 and x64 ComQuery results).
    /// </summary>
    public static IReadOnlyList<ComQueryLibrary> Merge(
        IEnumerable<ComQueryLibrary> x86Libs,
        IEnumerable<ComQueryLibrary> x64Libs)
    {
        var byGuid = new Dictionary<Guid, (ComQueryLibrary? X86, ComQueryLibrary? X64)>();

        foreach (var lib in x86Libs) {
            byGuid[lib.Guid] = (lib, null);
        }
        foreach (var lib in x64Libs) {
            byGuid.TryGetValue(lib.Guid, out var existing);
            byGuid[lib.Guid] = (existing.X86, lib);
        }

        var result = new List<ComQueryLibrary>(byGuid.Count);
        foreach (var (guid, (x86, x64)) in byGuid) {
            if (x86 == null) {
                result.Add(x64!);
            }
            else if (x64 == null) {
                result.Add(x86);
            }
            else {
                result.Add(MergeLibrary(x86, x64));
            }
        }

        return result;
    }

    static ComQueryLibrary MergeLibrary(ComQueryLibrary x86, ComQueryLibrary x64)
    {
        var mergedTypes = MergeTypes(x86.Types, x64.Types);
        var deps = UnionDeps(x86.DiscoveredDependencies, x64.DiscoveredDependencies);

        return x64 with {
            Types = mergedTypes,
            DiscoveredDependencies = deps,
        };
    }

    static IReadOnlyList<ComQueryType>? MergeTypes(
        IReadOnlyList<ComQueryType>? x86Types,
        IReadOnlyList<ComQueryType>? x64Types)
    {
        if (x86Types == null && x64Types == null) return null;
        if (x86Types == null) return x64Types;
        if (x64Types == null) return x86Types;

        var byName = new Dictionary<string, (ComQueryType? X86, ComQueryType? X64)>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in x86Types) byName[t.Name] = (t, null);
        foreach (var t in x64Types) {
            byName.TryGetValue(t.Name, out var existing);
            byName[t.Name] = (existing.X86, t);
        }

        var result = new List<ComQueryType>(byName.Count);
        foreach (var (_, (x86, x64)) in byName) {
            if (x86 == null) result.Add(x64!);
            else if (x64 == null) result.Add(x86);
            else result.Add(MergeType(x86, x64));
        }

        var x64Order = new HashSet<string>(x64Types.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var x64IndexMap = new Dictionary<string, int>(x64Types.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < x64Types.Count; i++)
            x64IndexMap[x64Types[i].Name] = i;

        return result
            .OrderBy(t => x64Order.Contains(t.Name) ? x64IndexMap[t.Name] : int.MaxValue)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static ComQueryType MergeType(ComQueryType x86, ComQueryType x64)
    {
        var mergedMembers = MergeMembers(x86.Members, x64.Members);
        var mergedEnumValues = MergeEnumValues(x86.EnumValues, x64.EnumValues);
        var interfaces = UnionStrings(x86.ImplementedInterfaces, x64.ImplementedInterfaces);

        return x64 with {
            Members = mergedMembers,
            EnumValues = mergedEnumValues,
            ImplementedInterfaces = interfaces,
        };
    }

    static IReadOnlyList<ComQueryMember>? MergeMembers(
        IReadOnlyList<ComQueryMember>? x86Members,
        IReadOnlyList<ComQueryMember>? x64Members)
    {
        if (x86Members == null && x64Members == null) return null;
        if (x86Members == null) return x64Members;
        if (x64Members == null) return x86Members;

        var byKey = new Dictionary<(string, LibraryMemberKind), (ComQueryMember? X86, ComQueryMember? X64)>(
            MemberKeyComparer.Instance);

        foreach (var m in x86Members) byKey[(m.Name, m.Kind)] = (m, null);
        foreach (var m in x64Members) {
            byKey.TryGetValue((m.Name, m.Kind), out var existing);
            byKey[(m.Name, m.Kind)] = (existing.X86, m);
        }

        var result = new List<ComQueryMember>(byKey.Count);
        foreach (var (_, (x86, x64)) in byKey) {
            if (x86 == null) result.Add(x64!);
            else if (x64 == null) result.Add(x86);
            else result.Add(PickMoreSpecificMember(x86, x64));
        }

        var x64Order = new Dictionary<(string, LibraryMemberKind), int>(MemberKeyComparer.Instance);
        for (int i = 0; i < x64Members.Count; i++)
            x64Order[(x64Members[i].Name, x64Members[i].Kind)] = i;

        return result
            .OrderBy(m => x64Order.TryGetValue((m.Name, m.Kind), out int idx) ? idx : int.MaxValue)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the member with the more specific return type.
    /// <c>object</c> is least specific; any other type wins.
    /// When both are non-<c>object</c> and differ, x64 is preferred.
    /// </summary>
    static ComQueryMember PickMoreSpecificMember(ComQueryMember x86, ComQueryMember x64)
    {
        bool x86IsObject = string.Equals(x86.ReturnType, "object", StringComparison.OrdinalIgnoreCase);
        bool x64IsObject = string.Equals(x64.ReturnType, "object", StringComparison.OrdinalIgnoreCase);

        if (x86IsObject && !x64IsObject) return x64;
        if (x64IsObject && !x86IsObject) return x86;
        return x64;
    }

    static IReadOnlyList<ComQueryEnumVal>? MergeEnumValues(
        IReadOnlyList<ComQueryEnumVal>? x86,
        IReadOnlyList<ComQueryEnumVal>? x64)
    {
        if (x86 == null && x64 == null) return null;
        if (x86 == null) return x64;
        if (x64 == null) return x86;

        var byName = new Dictionary<string, ComQueryEnumVal>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in x86) byName[v.Name] = v;
        foreach (var v in x64) byName[v.Name] = v;

        return byName.Values.OrderBy(v => v.Value).ToList();
    }

    static IReadOnlyList<ComQueryDiscoveredDep>? UnionDeps(
        IReadOnlyList<ComQueryDiscoveredDep>? x86,
        IReadOnlyList<ComQueryDiscoveredDep>? x64)
    {
        if (x86 == null && x64 == null) return null;
        var all = (x86 ?? []).Concat(x64 ?? []);
        return all.GroupBy(d => d.Guid).Select(g => g.First()).ToList();
    }

    static IReadOnlyList<string>? UnionStrings(IReadOnlyList<string>? x86, IReadOnlyList<string>? x64)
    {
        if (x86 == null && x64 == null) return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var s in (x64 ?? []).Concat(x86 ?? [])) {
            if (seen.Add(s)) result.Add(s);
        }
        return result.Count == 0 ? null : result;
    }

    sealed class MemberKeyComparer : IEqualityComparer<(string Name, LibraryMemberKind Kind)>
    {
        public static readonly MemberKeyComparer Instance = new();
        public bool Equals((string Name, LibraryMemberKind Kind) x, (string Name, LibraryMemberKind Kind) y)
            => string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) && x.Kind == y.Kind;
        public int GetHashCode((string Name, LibraryMemberKind Kind) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name), obj.Kind);
    }
}
