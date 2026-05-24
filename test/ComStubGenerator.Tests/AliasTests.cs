using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class AliasTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_AliasTypes_WritesNoFiles()
    {
        // Alias types are no longer written per-library (they are globalised by
        // ReferenceUsingsGenerator to avoid CS0105 duplicate-alias errors).
        var library = MakeLibrary("stdole2Tlb",
            new ComQueryType("OLE_HANDLE", LibraryTypeKind.Alias, [], [], AliasedType: "uint"),
            new ComQueryType("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedType: "uint"));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            written.Should().BeEmpty("alias-only libraries produce no files");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CollectAliases_ReturnsAllAliasesForLibrary()
    {
        var library = MakeLibrary("stdole2Tlb",
            new ComQueryType("OLE_HANDLE", LibraryTypeKind.Alias, [], [], AliasedType: "uint"),
            new ComQueryType("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedType: "uint"),
            new ComQueryType("MyDispatch", LibraryTypeKind.DispatchInterface, [], []));

        var aliases = ReferenceStubGenerator.CollectAliases(library);

        aliases.Should().HaveCount(2);
        aliases.Should().Contain(("OLE_HANDLE", "uint"));
        aliases.Should().Contain(("OLE_COLOR",  "uint"));
    }

    [TestMethod]
    public void CollectAliases_IgnoresTypesWithoutAliasedCSharpType()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("EmptyAlias", LibraryTypeKind.Alias, [], [], AliasedType: null),
            new ComQueryType("ValidAlias", LibraryTypeKind.Alias, [], [], AliasedType: "int"));

        var aliases = ReferenceStubGenerator.CollectAliases(library);

        aliases.Should().ContainSingle();
        aliases[0].Name.Should().Be("ValidAlias");
    }

    [TestMethod]
    public void Generate_MixedAliasAndClass_EmitsOnlyClassFile()
    {
        // Aliases are no longer written by Generate(); only real types produce files.
        var library = MakeLibrary("TestLib",
            new ComQueryType("OLE_HANDLE",  LibraryTypeKind.Alias,             [], [], AliasedType: "uint"),
            new ComQueryType("MyDispatch",  LibraryTypeKind.DispatchInterface,  [], []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            written[0].Should().EndWith("MyDispatch.cs");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
