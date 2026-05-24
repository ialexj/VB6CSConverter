using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class OutputTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_OutputPath_IsUnderLibNameSubfolder()
    {
        var library = MakeLibrary("ADODB",
            new ComQueryType("Connection", LibraryTypeKind.DispatchInterface,
                Members: [new("Open", LibraryMemberKind.Method, "void", [])],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle().Which
                .Should().Be(Path.Combine(tempDir, "ADODB", "Connection.cs"));
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_MultipleTypes_StableAlphabeticOrder()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("Zebra", LibraryTypeKind.DispatchInterface, [], []),
            new ComQueryType("Alpha", LibraryTypeKind.DispatchInterface, [], []),
            new ComQueryType("Mango", LibraryTypeKind.DispatchInterface, [], []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Select(Path.GetFileNameWithoutExtension)
                .Should().ContainInOrder("Alpha", "Mango", "Zebra");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
