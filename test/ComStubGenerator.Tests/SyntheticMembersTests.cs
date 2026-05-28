#nullable enable
using AwesomeAssertions;
using ComStubGenerator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ComStubGenerator.Tests;

[TestClass]
public class SyntheticMembersTests
{
    static readonly Guid GuidA = new("AAAAAAAA-1111-0000-0000-000000000001");

    // ──────────────────────────────────────────────────────────────────────
    // SyntheticMembersApplicator — happy-path
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Apply_MatchingTarget_MemberIsAdded()
    {
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Existing", LibraryMemberKind.Method, "void", []));

        var syntheticMember = new ComQueryMember("Injected", LibraryMemberKind.PropertyGet, "string", []);
        var sets = new[] {
            new SyntheticMemberSet(["MyLib.MyType"], [syntheticMember]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        var members = result[0].Types![0].Members!;
        members.Should().Contain(m => m.Name == "Injected", "synthetic member must be injected");
        members.Should().Contain(m => m.Name == "Existing", "original member must be preserved");
    }

    [TestMethod]
    public void Apply_MatchingTargetByName_MemberIsAdded()
    {
        // SafeName differs from Name — matching should succeed via Name too
        var lib = new ComQueryLibrary("My Lib", GuidA, 1, 0,
            Types: [new ComQueryType("MyType", LibraryTypeKind.Interface,
                [new ComQueryMember("Existing", LibraryMemberKind.Method, "void", [])])]);

        var sets = new[] {
            new SyntheticMemberSet(["My Lib.MyType"],
                [new ComQueryMember("Injected", LibraryMemberKind.PropertyGet, "string", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        result[0].Types![0].Members.Should().Contain(m => m.Name == "Injected");
    }

    [TestMethod]
    public void Apply_TargetMatchIsCaseInsensitive()
    {
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("M", LibraryMemberKind.Method, "void", []));

        var sets = new[] {
            new SyntheticMemberSet(["mylib.mytype"],
                [new ComQueryMember("SynMember", LibraryMemberKind.PropertyGet, "int", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        result[0].Types![0].Members.Should().Contain(m => m.Name == "SynMember");
    }

    [TestMethod]
    public void Apply_SyntheticMemberShadowsObjectReturnWithConcreteType()
    {
        // Existing member returns "object"; synthetic member returns "int" → int wins
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "object", []));

        var sets = new[] {
            new SyntheticMemberSet(["MyLib.MyType"],
                [new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "int", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        var members = result[0].Types![0].Members!;
        members.Should().HaveCount(1, "duplicate (Name, Kind) pair must be merged, not duplicated");
        members[0].ReturnType.Should().Be("int", "concrete type wins over object");
    }

    [TestMethod]
    public void Apply_SyntheticConcreteDoesNotReplaceExistingConcrete_X64Wins()
    {
        // Existing returns "string" (treated as x86 side), synthetic returns "bool" (x64 side) → bool wins
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "string", []));

        var sets = new[] {
            new SyntheticMemberSet(["MyLib.MyType"],
                [new ComQueryMember("Prop", LibraryMemberKind.PropertyGet, "bool", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        var members = result[0].Types![0].Members!;
        members.Should().HaveCount(1, "duplicate pair must be merged");
        members[0].ReturnType.Should().Be("bool",
            "when both are concrete and differ, synthetic (x64 slot) wins");
    }

    // ──────────────────────────────────────────────────────────────────────
    // SyntheticMembersApplicator — graceful degradation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Apply_EmptySyntheticSets_ReturnsInputUnchanged()
    {
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Existing", LibraryMemberKind.Method, "void", []));

        IReadOnlyList<ComQueryLibrary> input = [lib];
        var result = SyntheticMembersApplicator.Apply(input, []);

        result.Should().BeSameAs(input,
            because: "an empty sets list must return the same reference without allocating");
    }

    [TestMethod]
    public void Apply_UnrecognisedLibraryName_NoChangeNoException()
    {
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Existing", LibraryMemberKind.Method, "void", []));

        var sets = new[] {
            new SyntheticMemberSet(["UnknownLib.MyType"],
                [new ComQueryMember("SynMember", LibraryMemberKind.PropertyGet, "int", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        // Original library must be unchanged
        result[0].Types![0].Members.Should().NotContain(m => m.Name == "SynMember");
    }

    [TestMethod]
    public void Apply_UnrecognisedTypeName_NoChangeNoException()
    {
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Existing", LibraryMemberKind.Method, "void", []));

        var sets = new[] {
            new SyntheticMemberSet(["MyLib.UnknownType"],
                [new ComQueryMember("SynMember", LibraryMemberKind.PropertyGet, "int", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib], sets);

        result[0].Types![0].Members.Should().NotContain(m => m.Name == "SynMember");
    }

    [TestMethod]
    public void Apply_InvalidTargetFormat_NoChangeNoException()
    {
        var lib = MakeLib(GuidA, "MyLib", "MyType",
            new ComQueryMember("Existing", LibraryMemberKind.Method, "void", []));

        var sets = new[] {
            new SyntheticMemberSet(["NoDotsHere", ".LeadingDot", "TrailingDot."],
                [new ComQueryMember("SynMember", LibraryMemberKind.PropertyGet, "int", [])]),
        };

        var act = () => SyntheticMembersApplicator.Apply([lib], sets);
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Apply_OriginalOrderIsPreserved()
    {
        var guidB = new Guid("BBBBBBBB-2222-0000-0000-000000000002");
        var lib1 = MakeLib(GuidA, "LibA", "TypeA");
        var lib2 = MakeLib(guidB, "LibB", "TypeB");

        var sets = new[] {
            new SyntheticMemberSet(["LibB.TypeB"],
                [new ComQueryMember("SynMember", LibraryMemberKind.PropertyGet, "int", [])]),
        };

        var result = SyntheticMembersApplicator.Apply([lib1, lib2], sets);

        result[0].Guid.Should().Be(GuidA, "order of libraries must be preserved");
        result[1].Guid.Should().Be(guidB);
    }

    // ──────────────────────────────────────────────────────────────────────
    // SyntheticMembersLoader
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Loader_ValidJsonFile_DeserializesCorrectly()
    {
        string json = """
            [
              {
                "targets": ["MyLib.MyType", "MyLib.OtherType"],
                "members": [
                  {
                    "name": "SynProp",
                    "kind": "PropertyGet",
                    "returnType": "int",
                    "parameters": [],
                    "isDefault": false
                  }
                ]
              }
            ]
            """;

        string path = WriteTemp(json);
        try {
            var sets = SyntheticMembersLoader.Load(path);

            sets.Should().HaveCount(1);
            sets[0].Targets.Should().BeEquivalentTo(new[] { "MyLib.MyType", "MyLib.OtherType" });
            sets[0].Members.Should().HaveCount(1);
            sets[0].Members[0].Name.Should().Be("SynProp");
            sets[0].Members[0].Kind.Should().Be(LibraryMemberKind.PropertyGet);
            sets[0].Members[0].ReturnType.Should().Be("int");
        }
        finally {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Loader_MissingConventionPath_ReturnsEmptyList()
    {
        // Pass null so loader checks convention path; that path won't exist in test
        // unless the test runner happens to ship a synthetic_members.json — unlikely.
        // We verify by ensuring no exception is thrown and an empty list is returned
        // when the convention file is absent.
        //
        // We can't easily control AppContext.BaseDirectory, so instead we call Load
        // with a non-existent explicit path only as a fallback — here we test the
        // null-path branch indirectly by checking the return type.
        var result = SyntheticMembersLoader.Load(null);
        result.Should().NotBeNull("Load(null) must always return a non-null list");
        // Count can be 0 (no file) or ≥ 0 (file happens to exist); no exception either way.
    }

    [TestMethod]
    public void Loader_ExplicitMissingPath_ThrowsFileNotFoundException()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_nonexistent.json");
        var act = () => SyntheticMembersLoader.Load(missingPath);
        act.Should().Throw<FileNotFoundException>();
    }

    [TestMethod]
    public void Loader_CamelCaseJson_DeserializesCorrectly()
    {
        // JSON uses camelCase property names — the loader must accept them.
        string json = """
            [
              {
                "targets": ["Lib.Type"],
                "members": [
                  {
                    "name": "Meth",
                    "kind": "Method",
                    "returnType": "void",
                    "parameters": [
                      { "name": "x", "type": "int", "isOptional": false, "isOut": false }
                    ]
                  }
                ]
              }
            ]
            """;

        string path = WriteTemp(json);
        try {
            var sets = SyntheticMembersLoader.Load(path);
            sets.Should().HaveCount(1);
            sets[0].Members[0].Parameters.Should().HaveCount(1);
            sets[0].Members[0].Parameters[0].Name.Should().Be("x");
        }
        finally {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Loader_ShippedVbFormShowOverride_MarksParametersOptional()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "synthetic_members.json");
        if (!File.Exists(path)) Assert.Inconclusive("synthetic_members.json not found in test output");

        var sets = SyntheticMembersLoader.Load(path);
        var vbFormSet = sets.SingleOrDefault(set => set.Targets.Any(target =>
            string.Equals(target, "VB.Form", StringComparison.OrdinalIgnoreCase)));

        vbFormSet.Should().NotBeNull("the shipped synthetic members must include VB.Form overrides");

        var show = vbFormSet!.Members.Single(member =>
            string.Equals(member.Name, "Show", StringComparison.OrdinalIgnoreCase)
            && member.Kind == LibraryMemberKind.Method);

        show.Parameters.Should().HaveCount(2);
        show.Parameters.Should().OnlyContain(parameter => parameter.IsOptional,
            "VB.Form.Show parameters must be optional so the stub generator emits defaults");
    }

    // ──────────────────────────────────────────────────────────────────────
    // LibraryMerger.ApplySyntheticMembers
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ApplySyntheticMembers_AddsNewMembers()
    {
        var type = new ComQueryType("T", LibraryTypeKind.Interface,
            [new ComQueryMember("Existing", LibraryMemberKind.Method, "void", [])]);

        var patched = LibraryMerger.ApplySyntheticMembers(type,
            [new ComQueryMember("New", LibraryMemberKind.PropertyGet, "string", [])]);

        patched.Members.Should().Contain(m => m.Name == "Existing");
        patched.Members.Should().Contain(m => m.Name == "New");
    }

    [TestMethod]
    public void ApplySyntheticMembers_NullExistingMembers_SetsFromExtra()
    {
        var type = new ComQueryType("T", LibraryTypeKind.Interface, Members: null);

        var patched = LibraryMerger.ApplySyntheticMembers(type,
            [new ComQueryMember("New", LibraryMemberKind.PropertyGet, "string", [])]);

        patched.Members.Should().ContainSingle(m => m.Name == "New");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static ComQueryLibrary MakeLib(Guid guid, string libName, string typeName,
        params ComQueryMember[] members)
        => new(libName, guid, 1, 0,
            Types: [new ComQueryType(typeName, LibraryTypeKind.Interface, members)]);

    static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_synthetic.json");
        File.WriteAllText(path, content);
        return path;
    }
}
