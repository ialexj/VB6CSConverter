using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class EventTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_AddRemovePair_Interface_CollapsedToEvent()
    {
        var library = MakeDotnetLibrary("TestLib",
            new ComQueryType("IWidget", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("add_Disposed",    LibraryMemberKind.Method, "void", [new("value", "System.EventHandler", false, false)]),
                    new("remove_Disposed", LibraryMemberKind.Method, "void", [new("value", "System.EventHandler", false, false)]),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("event System.EventHandler Disposed",
                "add_/remove_ pair must be collapsed into an event declaration");
            source.Should().NotContain("add_Disposed",
                "add_ method must be removed after collapsing");
            source.Should().NotContain("remove_Disposed",
                "remove_ method must be removed after collapsing");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_AddRemovePair_Class_CollapsedToPublicEvent()
    {
        var library = MakeDotnetLibrary("TestLib",
            new ComQueryType("Widget", LibraryTypeKind.Class,
                Members: [
                    new("add_Disposed",    LibraryMemberKind.Method, "void", [new("value", "System.EventHandler", false, false)]),
                    new("remove_Disposed", LibraryMemberKind.Method, "void", [new("value", "System.EventHandler", false, false)]),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("public event System.EventHandler Disposed",
                "collapsed event on a class must carry the public modifier");
            source.Should().NotContain("add_Disposed");
            source.Should().NotContain("remove_Disposed");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_UnpairedAddMethod_LeftAsMethod()
    {
        // add_Foo with no remove_Foo counterpart must not be touched.
        var library = MakeLibrary("TestLib",
            new ComQueryType("IWidget", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("add_Foo", LibraryMemberKind.Method, "void", [new("value", "System.EventHandler", false, false)]),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("add_Foo",
                "unpaired add_ method must remain as a regular method");
            source.Should().NotContain("event",
                "no event declaration must be emitted for an unpaired add_");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_AddRemovePairWithMismatchedTypes_LeftAsMethods()
    {
        // add_Foo(EventHandler) + remove_Foo(Action) — parameter types differ → not collapsed.
        var library = MakeLibrary("TestLib",
            new ComQueryType("IWidget", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("add_Foo",    LibraryMemberKind.Method, "void", [new("value", "System.EventHandler", false, false)]),
                    new("remove_Foo", LibraryMemberKind.Method, "void", [new("value", "System.Action",       false, false)]),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("add_Foo",    "mismatched-type pair must leave add_ as a method");
            source.Should().Contain("remove_Foo", "mismatched-type pair must leave remove_ as a method");
            source.Should().NotContain("event",   "no event declaration must be emitted");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
