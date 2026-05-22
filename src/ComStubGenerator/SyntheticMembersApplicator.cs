#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ComStubGenerator;

/// <summary>
/// Applies <see cref="SyntheticMemberSet"/> definitions to a merged library collection,
/// injecting extra members into matched types using the same union/specificity rules as
/// <see cref="LibraryMerger"/>.
/// </summary>
public static class SyntheticMembersApplicator
{
    /// <summary>
    /// Returns a new library collection with synthetic members merged into every type
    /// matched by the <paramref name="syntheticSets"/> targets.
    /// </summary>
    /// <remarks>
    /// Each target is a string of the form <c>LibraryName.TypeName</c> (case-insensitive).
    /// Library matching checks both <see cref="ComQueryLibrary.SafeName"/> and
    /// <see cref="ComQueryLibrary.Name"/>. Unrecognised library or type names produce a
    /// warning log entry and are otherwise ignored; the method never throws for missing targets.
    /// </remarks>
    public static IReadOnlyList<ComQueryLibrary> Apply(
        IReadOnlyList<ComQueryLibrary> merged,
        IReadOnlyList<SyntheticMemberSet> syntheticSets)
    {
        if (syntheticSets.Count == 0) return merged;

        // Build a mutable index by GUID so we can patch libraries incrementally.
        var libsByGuid = merged.ToDictionary(l => l.Guid);

        foreach (var set in syntheticSets) {
            if (set.Members == null || set.Members.Count == 0) continue;

            foreach (var target in set.Targets) {
                if (!TryParseTarget(target, out string libPart, out string typePart)) {
                    Log.Default.Warning(
                        "SyntheticMembersApplicator: invalid target '{target}' — expected 'LibraryName.TypeName'",
                        target);
                    continue;
                }

                var library = libsByGuid.Values.FirstOrDefault(l =>
                    string.Equals(l.SafeName, libPart, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.Name,     libPart, StringComparison.OrdinalIgnoreCase));

                if (library == null) {
                    Log.Default.Warning(
                        "SyntheticMembersApplicator: library '{lib}' not found (target '{target}')",
                        libPart, target);
                    continue;
                }

                var types = library.Types;
                if (types == null || types.Count == 0) {
                    Log.Default.Warning(
                        "SyntheticMembersApplicator: type '{type}' not found in library '{lib}' (library has no types)",
                        typePart, libPart);
                    continue;
                }

                int typeIdx = -1;
                for (int i = 0; i < types.Count; i++) {
                    if (string.Equals(types[i].Name, typePart, StringComparison.OrdinalIgnoreCase)) {
                        typeIdx = i;
                        break;
                    }
                }

                if (typeIdx < 0) {
                    Log.Default.Warning(
                        "SyntheticMembersApplicator: type '{type}' not found in library '{lib}'",
                        typePart, libPart);
                    continue;
                }

                var patchedType = LibraryMerger.ApplySyntheticMembers(types[typeIdx], set.Members);

                var newTypes = types.ToList();
                newTypes[typeIdx] = patchedType;

                libsByGuid[library.Guid] = library with { Types = newTypes };
            }
        }

        // Preserve original ordering from merged.
        return merged.Select(l => libsByGuid[l.Guid]).ToList();
    }

    static bool TryParseTarget(string target, out string libPart, out string typePart)
    {
        int dot = target.IndexOf('.');
        if (dot <= 0 || dot == target.Length - 1) {
            libPart = typePart = string.Empty;
            return false;
        }

        libPart  = target[..dot];
        typePart = target[(dot + 1)..];
        return true;
    }
}
