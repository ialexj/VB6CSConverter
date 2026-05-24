using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class EnumTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_EnumType_EmitsEnumDeclaration()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyEnum", LibraryTypeKind.Enum,
                Members: [],
                EnumValues: [
                    new("ValueA", 0),
                    new("ValueB", 1),
                    new("ValueC", 3),
                ]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var filePath = written[0];
            filePath.Should().EndWith(Path.Combine("TestLib", "MyEnum.cs"));

            var source = File.ReadAllText(filePath);
            source.Should().Contain("public enum MyEnum");
            source.Should().Contain("ValueA = 0");
            source.Should().Contain("ValueB = 1");
            source.Should().Contain("ValueC = 3");
        }
        finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
