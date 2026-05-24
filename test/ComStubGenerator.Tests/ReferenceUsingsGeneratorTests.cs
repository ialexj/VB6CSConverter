using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class ReferenceUsingsGeneratorTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void GenerateReferenceUsings_WritesNamespaceAndEnumUsings()
    {
        var libA = MakeLibrary("ADODB",
            new ComQueryType("CursorTypeEnum", LibraryTypeKind.Enum, [], [
                new("ForwardOnly", 0),
            ]),
            new ComQueryType("Connection", LibraryTypeKind.DispatchInterface, [], []));

        var libB = MakeLibrary("MSComctlLib",
            new ComQueryType("ListViewConstants", LibraryTypeKind.Enum, [], [
                new("lvwIcon", 0),
            ]),
            new ComQueryType("ListView", LibraryTypeKind.DispatchInterface, [], []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var filePath = ReferenceUsingsGenerator.Generate([libB, libA], tempDir);

            filePath.Should().Be(Path.Combine(tempDir, "_ReferenceUsings.cs"));
            File.Exists(filePath).Should().BeTrue();

            var source = File.ReadAllText(filePath);
            source.Should().Contain("global using ADODB;");
            source.Should().Contain("global using MSComctlLib;");
            source.Should().Contain("global using static ADODB.CursorTypeEnum;");
            source.Should().Contain("global using static MSComctlLib.ListViewConstants;");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateReferenceUsings_DeduplicatesAndSortsEntries()
    {
        var enumType = new ComQueryType("Constants", LibraryTypeKind.Enum, [], [
            new("ValueA", 0),
        ]);

        var libA = MakeLibrary("ZZLib", enumType);
        var libB = MakeLibrary("AALib", enumType);
        var libC = MakeLibrary("ZZLib", enumType);

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var filePath = ReferenceUsingsGenerator.Generate([libA, libB, libC], tempDir);
            var lines = File.ReadAllLines(filePath);

            lines.Count(l => l == "global using AALib;").Should().Be(1);
            lines.Count(l => l == "global using ZZLib;").Should().Be(1);
            lines.Count(l => l == "global using static ZZLib.Constants;").Should().Be(1);

            var aaUsingIndex = Array.IndexOf(lines, "global using AALib;");
            var zzUsingIndex = Array.IndexOf(lines, "global using ZZLib;");
            aaUsingIndex.Should().BeLessThan(zzUsingIndex);
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateReferenceUsings_DeduplicatesAliasesAcrossLibraries()
    {
        // Simulates stdole and oleaut32 both declaring OLE_COLOR — only one global using should appear.
        var libA = MakeLibrary("StdOle",
            new ComQueryType("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedType: "uint"),
            new ComQueryType("OLE_HANDLE", LibraryTypeKind.Alias, [], [], AliasedType: "uint"));
        var libB = MakeLibrary("OleAut32",
            new ComQueryType("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedType: "uint"));

        var aliases = new[] { libA, libB }
            .SelectMany(l => ReferenceStubGenerator.CollectAliases(l));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var filePath = ReferenceUsingsGenerator.Generate([libA, libB], tempDir, aliases);
            var source = File.ReadAllText(filePath);

            // Exactly one declaration for OLE_COLOR despite two libraries exporting it.
            source.Split('\n').Count(l => l.TrimEnd() == "global using OLE_COLOR = uint;")
                .Should().Be(1, "duplicate alias across libraries must be emitted only once");
            source.Should().Contain("global using OLE_HANDLE = uint;");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateReferenceUsings_TransitiveLibrariesAreIncluded()
    {
        // Program.cs now passes ALL merged libraries (direct + transitive) to Generate() so that
        // transitive libraries such as CTHYPERLINKLibCtl and FontLibCtl get their namespace
        // usings emitted and remain reachable in the converted project.
        var directLib = MakeLibrary("ADODB",
            new ComQueryType("CursorTypeEnum", LibraryTypeKind.Enum, [], [new("ForwardOnly", 0)]),
            new ComQueryType("Connection", LibraryTypeKind.DispatchInterface, [], []));

        var transitiveLib = new ComQueryLibrary("CTHYPERLINKLibCtl", Guid.NewGuid(), 1, 0,
            IsTransitive: true,
            Types: [new ComQueryType("CTHyperlink", LibraryTypeKind.DispatchInterface,
                Members: [new("Url", LibraryMemberKind.PropertyGet, "string", [])],
                EnumValues: [])]);

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            // Program.cs passes all merged libraries — transitive ones must not be silently dropped.
            var allLibraries = new[] { directLib, transitiveLib };
            var filePath = ReferenceUsingsGenerator.Generate(allLibraries, tempDir);

            var source = File.ReadAllText(filePath);
            source.Should().Contain("global using ADODB;");
            source.Should().Contain("global using CTHYPERLINKLibCtl;",
                "transitive libraries must get a namespace using so their types are reachable");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
