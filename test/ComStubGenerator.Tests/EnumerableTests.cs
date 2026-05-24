using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class EnumerableTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_DispatchInterfaceWithIEnumerable_EmitsBaseAndGetEnumerator()
    {
        // Simulates a COM dispatch interface whose _NewEnum was replaced by the inspector with
        // GetEnumerator + IEnumerable in ImplementedInterfaces (e.g. VBA.Collection).
        var library = MakeLibrary("VBA",
            new ComQueryType("Collection", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Count", LibraryMemberKind.PropertyGet, "int", []),
                    new("GetEnumerator", LibraryMemberKind.Method, "System.Collections.IEnumerator", []),
                ],
                EnumValues: [],
                ImplementedInterfaces: ["System.Collections.IEnumerable"]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("System.Collections.IEnumerable",
                "the interface must declare IEnumerable in its base list");
            source.Should().NotContain("GetEnumerator",
                "GetEnumerator is already declared by IEnumerable; re-declaring it in the derived interface shadows the inherited member (CS0108)");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ClassWithIEnumerable_EmitsBaseAndGetEnumerator()
    {
        // Simulates a COM coclass whose default interface has _NewEnum.
        var library = MakeLibrary("DAO",
            new ComQueryType("Fields", LibraryTypeKind.Class,
                Members: [
                    new("Count", LibraryMemberKind.PropertyGet, "int", []),
                    new("GetEnumerator", LibraryMemberKind.Method, "System.Collections.IEnumerator", []),
                ],
                EnumValues: [],
                ImplementedInterfaces: ["System.Collections.IEnumerable"]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("System.Collections.IEnumerable");
            source.Should().Contain("GetEnumerator");
            source.Should().Contain("NotImplementedException",
                "GetEnumerator body must throw NotImplementedException");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
