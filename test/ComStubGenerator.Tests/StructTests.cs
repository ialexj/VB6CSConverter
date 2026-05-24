using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class StructTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_StructType_EmitsStructDeclaration()
    {
        var library = MakeLibrary("Win",
            new ComQueryType("LOGPALETTE256", LibraryTypeKind.Struct,
                Members: [
                    new("palVersion",    LibraryMemberKind.Field, "short",   []),
                    new("palNumEntries", LibraryMemberKind.Field, "short",   []),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var file = written[0];
            Path.GetFileNameWithoutExtension(file).Should().Be("LOGPALETTE256");

            var source = File.ReadAllText(file);
            source.Should().Contain("struct LOGPALETTE256", "a TKIND_RECORD must become a C# struct");
            source.Should().Contain("public short palVersion");
            source.Should().Contain("public short palNumEntries");
            source.Should().Contain("namespace Win", "stub must be in the library's namespace");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_StructType_NoFields_EmitsEmptyStruct()
    {
        var library = MakeLibrary("Win",
            new ComQueryType("POINT", LibraryTypeKind.Struct, [], []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("struct POINT");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
