#nullable enable
using AwesomeAssertions;
using ComStubGenerator;
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComStubGenerator.Tests;

[TestClass]
public class LibraryMergerTests
{
    static readonly Guid GuidA = new("AAAAAAAA-0000-0000-0000-000000000001");
    static readonly Guid GuidB = new("BBBBBBBB-0000-0000-0000-000000000002");
    static readonly Guid GuidC = new("CCCCCCCC-0000-0000-0000-000000000003");

    // ──────────────────────────────────────────────────────────────────────
    // Library-level merging
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_X86OnlyLibrary_IncludedInResult()
    {
        var x86 = new ComQueryLibrary("LibA", GuidA, 1, 0);
        var merged = LibraryMerger.Merge([x86], []);
        merged.Should().ContainSingle(l => l.Guid == GuidA);
    }

    [TestMethod]
    public void Merge_X64OnlyLibrary_IncludedInResult()
    {
        var x64 = new ComQueryLibrary("LibC", GuidC, 1, 0);
        var merged = LibraryMerger.Merge([], [x64]);
        merged.Should().ContainSingle(l => l.Guid == GuidC);
    }

    [TestMethod]
    public void Merge_BothArchLibraries_MergedByGuid()
    {
        var x86 = new ComQueryLibrary("LibB", GuidB, 1, 0);
        var x64 = new ComQueryLibrary("LibB", GuidB, 1, 0);
        var merged = LibraryMerger.Merge([x86], [x64]);
        merged.Should().ContainSingle(l => l.Guid == GuidB, "same GUID must be merged into one library");
    }

    [TestMethod]
    public void Merge_AllThreeLibraries_CorrectCount()
    {
        // x86 has LibA + LibB; x64 has LibB + LibC → result should have LibA + LibB + LibC
        var x86Libs = new ComQueryLibrary[] {
            new("LibA", GuidA, 1, 0),
            new("LibB", GuidB, 1, 0),
        };
        var x64Libs = new ComQueryLibrary[] {
            new("LibB", GuidB, 1, 0),
            new("LibC", GuidC, 1, 0),
        };

        var merged = LibraryMerger.Merge(x86Libs, x64Libs);
        merged.Should().HaveCount(3);
        merged.Should().Contain(l => l.Guid == GuidA, "x86-only LibA must be included");
        merged.Should().Contain(l => l.Guid == GuidB, "shared LibB must be included once");
        merged.Should().Contain(l => l.Guid == GuidC, "x64-only LibC must be included");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Member-level merging: type specificity
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_X86ReturnsObject_X64ReturnsConcrete_PicksX64()
    {
        // x86 PropB2 returns object; x64 PropB2 returns int → int wins
        var x86Lib = MakeLib(GuidB, "T2", new ComQueryMember("PropB2", LibraryMemberKind.PropertyGet, "object", []));
        var x64Lib = MakeLib(GuidB, "T2", new ComQueryMember("PropB2", LibraryMemberKind.PropertyGet, "int",    []));

        var merged = LibraryMerger.Merge([x86Lib], [x64Lib]);
        var member = merged[0].Types![0].Members![0];
        member.ReturnType.Should().Be("int", "x64's more specific type should win over x86's object");
    }

    [TestMethod]
    public void Merge_X64ReturnsObject_X86ReturnsConcrete_PicksX86()
    {
        // x64 PropA1 returns object; x86 PropA1 returns int → int wins
        var x86Lib = MakeLib(GuidA, "T1", new ComQueryMember("PropA1", LibraryMemberKind.PropertyGet, "int",    []));
        var x64Lib = MakeLib(GuidA, "T1", new ComQueryMember("PropA1", LibraryMemberKind.PropertyGet, "object", []));

        var merged = LibraryMerger.Merge([x86Lib], [x64Lib]);
        var member = merged[0].Types![0].Members![0];
        member.ReturnType.Should().Be("int", "x86's more specific type should win over x64's object");
    }

    [TestMethod]
    public void Merge_BothObjectReturnType_X64Wins()
    {
        // Both return object → x64 wins (tiebreaker)
        var x86Lib = MakeLib(GuidB, "T2", new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "object", []));
        var x64Lib = MakeLib(GuidB, "T2", new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "object", []));

        var merged = LibraryMerger.Merge([x86Lib], [x64Lib]);
        var member = merged[0].Types![0].Members![0];
        member.ReturnType.Should().Be("object");
    }

    [TestMethod]
    public void Merge_BothConcreteReturnTypes_X64Wins()
    {
        // x86 returns string, x64 returns int → x64 wins (tiebreaker)
        var x86Lib = MakeLib(GuidB, "T2", new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "string", []));
        var x64Lib = MakeLib(GuidB, "T2", new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "int",    []));

        var merged = LibraryMerger.Merge([x86Lib], [x64Lib]);
        var member = merged[0].Types![0].Members![0];
        member.ReturnType.Should().Be("int", "when both are concrete and differ, x64 wins");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Member union (arch-only members are preserved)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_X86OnlyMember_IncludedInResult()
    {
        var x86Lib = MakeLib(GuidA, "T1", new ComQueryMember("X86OnlyProp", LibraryMemberKind.PropertyGet, "bool", []));
        var x64Lib = new ComQueryLibrary("Lib", GuidA, 1, 0,
            Types: [new ComQueryType("T1", LibraryTypeKind.Interface, [])]);

        var merged = LibraryMerger.Merge([x86Lib], [x64Lib]);
        merged[0].Types![0].Members.Should().Contain(m => m.Name == "X86OnlyProp",
            "x86-only members must be included in the union");
    }

    [TestMethod]
    public void Merge_X64OnlyMember_IncludedInResult()
    {
        var x86Lib = new ComQueryLibrary("Lib", GuidA, 1, 0,
            Types: [new ComQueryType("T1", LibraryTypeKind.Interface, [])]);
        var x64Lib = MakeLib(GuidA, "T1", new ComQueryMember("X64OnlyProp", LibraryMemberKind.PropertyGet, "bool", []));

        var merged = LibraryMerger.Merge([x86Lib], [x64Lib]);
        merged[0].Types![0].Members.Should().Contain(m => m.Name == "X64OnlyProp",
            "x64-only members must be included in the union");
    }

    // ──────────────────────────────────────────────────────────────────────
    // OCA + OCX: same GUID, different names must both survive the merge
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Merge_SameGuidDifferentNames_BothPreserved()
    {
        // An OCX and its companion OCA share the same GUID but carry different library
        // names (e.g. "ActiveBarLibrary" vs "ActiveBarLibraryCtl").  Both must survive
        // the merge rather than one silently overwriting the other.
        var x86Oca = new ComQueryLibrary("ActiveBarLibraryCtl", GuidA, 1, 0);
        var x86Ocx = new ComQueryLibrary("ActiveBarLibrary",    GuidA, 1, 0);
        var x64Oca = new ComQueryLibrary("ActiveBarLibraryCtl", GuidA, 1, 0);
        var x64Ocx = new ComQueryLibrary("ActiveBarLibrary",    GuidA, 1, 0);

        var merged = LibraryMerger.Merge([x86Oca, x86Ocx], [x64Oca, x64Ocx]);

        merged.Should().HaveCount(2,
            "OCA and OCX libraries with the same GUID but different names must both be preserved");
        merged.Should().Contain(l => l.Name == "ActiveBarLibraryCtl",
            "VB6-facing OCA library must be present");
        merged.Should().Contain(l => l.Name == "ActiveBarLibrary",
            "automation OCX library must be present");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static ComQueryLibrary MakeLib(Guid guid, string typeName, params ComQueryMember[] members)
        => new("Lib", guid, 1, 0,
            Types: [new ComQueryType(typeName, LibraryTypeKind.Interface, members)]);
}
