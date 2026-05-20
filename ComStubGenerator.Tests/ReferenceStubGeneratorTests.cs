using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class ReferenceStubGeneratorTests
{
    static readonly Guid TestGuid = new("12345678-0000-0000-0000-000000000001");

    // ──────────────────────────────────────────────────────────────────────
    // MakeSafeName
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MakeSafeName_WithSpaces_ProducesCamelCase()
    {
        ReferenceNaming.MakeSafeName("Microsoft Scripting Runtime")
            .Should().Be("MicrosoftScriptingRuntime");
    }

    [TestMethod]
    public void MakeSafeName_Empty_ReturnsUnknownLib()
    {
        ReferenceNaming.MakeSafeName("").Should().Be("UnknownLib");
    }

    [TestMethod]
    public void MakeSafeName_WithDots_ProducesCamelCase()
    {
        ReferenceNaming.MakeSafeName("stdole2.tlb")
            .Should().Be("stdole2Tlb");
    }

    // ──────────────────────────────────────────────────────────────────────
    // MakeSafeIdentifier
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MakeSafeIdentifier_CSharpKeyword_GetsAtPrefix()
    {
        ReferenceStubGenerator.MakeSafeIdentifier("object").Should().Be("@object");
        ReferenceStubGenerator.MakeSafeIdentifier("string").Should().Be("@string");
        ReferenceStubGenerator.MakeSafeIdentifier("ref").Should().Be("@ref");
    }

    [TestMethod]
    public void MakeSafeIdentifier_NormalName_Unchanged()
    {
        ReferenceStubGenerator.MakeSafeIdentifier("Count").Should().Be("Count");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Enum generation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_EnumType_EmitsEnumDeclaration()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("MyEnum", LibraryTypeKind.Enum,
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
            source.Should().Contain("GeneratedCode");
        }
        finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Interface / class generation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_DispatchInterface_EmitsInterfaceWithMembers()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("Recordset", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("MoveNext", LibraryMemberKind.Method, "void", []),
                    new("Open",     LibraryMemberKind.Method, "void", [
                        new("Source",  "string", IsOptional: true,  IsOut: false),
                        new("Options", "int",    IsOptional: true,  IsOut: false),
                    ]),
                    new("EOF", LibraryMemberKind.PropertyGet, "bool", []),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("public interface Recordset");
            source.Should().Contain("MoveNext");
            source.Should().Contain("Open");
            source.Should().Contain("bool EOF");
            source.Should().NotContain("NotImplementedException");
        }
        finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Generate_Interface_EmitsInterfaceDeclaration()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("IAnimation", LibraryTypeKind.Interface,
                Members: [
                    new("Play", LibraryMemberKind.Method, "void", []),
                    new("Visible", LibraryMemberKind.PropertyGet, "bool", []),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("public interface IAnimation");
            source.Should().Contain("void Play(");
            source.Should().Contain("bool Visible");
            source.Should().NotContain("NotImplementedException");
        }
        finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Generate_PropertyGetAndSet_EmittedAsSingleProperty()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("Widget", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Caption", LibraryMemberKind.PropertyGet, "string", []),
                    new("Caption", LibraryMemberKind.PropertySet, "void",   [new("Value", "string", false, false)]),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            // Should have get AND set in one property block — count "Caption" occurrences
            var captionCount = System.Text.RegularExpressions.Regex.Matches(source, @"\bCaption\b").Count;
            captionCount.Should().Be(1, "get + set should be merged into one property declaration");
            source.Should().Contain("get");
            source.Should().Contain("set");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_Module_EmitsStaticClass()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("MathUtils", LibraryTypeKind.Module,
                Members: [new("Add", LibraryMemberKind.Method, "int", [
                    new("a", "int", false, false),
                    new("b", "int", false, false),
                ])],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("public static class MathUtils");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_Class_ImplementsConfiguredInterfaces()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("Animation", LibraryTypeKind.Class,
                Members: [new("Play", LibraryMemberKind.Method, "void", [])],
                EnumValues: [],
                ImplementedInterfaces: ["IAnimation", "IDispatch"]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("public class Animation : IAnimation, IDispatch");
            source.Should().Contain("NotImplementedException");
        }
        finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Alias (TKIND_ALIAS) — CollectAliases / Generate behaviour
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_AliasTypes_WritesNoFiles()
    {
        // Alias types are no longer written per-library (they are globalised by
        // ReferenceUsingsGenerator to avoid CS0105 duplicate-alias errors).
        var library = MakeLibrary("stdole2Tlb",
            new LibraryTypeModel("OLE_HANDLE", LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"),
            new LibraryTypeModel("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"));

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
            new LibraryTypeModel("OLE_HANDLE", LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"),
            new LibraryTypeModel("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"),
            new LibraryTypeModel("MyDispatch", LibraryTypeKind.DispatchInterface, [], []));

        var aliases = ReferenceStubGenerator.CollectAliases(library);

        aliases.Should().HaveCount(2);
        aliases.Should().Contain(("OLE_HANDLE", "uint"));
        aliases.Should().Contain(("OLE_COLOR",  "uint"));
    }

    [TestMethod]
    public void CollectAliases_IgnoresTypesWithoutAliasedCSharpType()
    {
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("EmptyAlias", LibraryTypeKind.Alias, [], [], AliasedCSharpType: null),
            new LibraryTypeModel("ValidAlias", LibraryTypeKind.Alias, [], [], AliasedCSharpType: "int"));

        var aliases = ReferenceStubGenerator.CollectAliases(library);

        aliases.Should().ContainSingle();
        aliases[0].Name.Should().Be("ValidAlias");
    }

    [TestMethod]
    public void Generate_MixedAliasAndClass_EmitsOnlyClassFile()
    {
        // Aliases are no longer written by Generate(); only real types produce files.
        var library = MakeLibrary("TestLib",
            new LibraryTypeModel("OLE_HANDLE",  LibraryTypeKind.Alias,             [], [], AliasedCSharpType: "uint"),
            new LibraryTypeModel("MyDispatch",  LibraryTypeKind.DispatchInterface,  [], []));

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

    // ──────────────────────────────────────────────────────────────────────
    // Output path structure
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_OutputPath_IsUnderLibNameSubfolder()
    {
        var library = MakeLibrary("ADODB",
            new LibraryTypeModel("Connection", LibraryTypeKind.DispatchInterface,
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
            new LibraryTypeModel("Zebra", LibraryTypeKind.DispatchInterface, [], []),
            new LibraryTypeModel("Alpha", LibraryTypeKind.DispatchInterface, [], []),
            new LibraryTypeModel("Mango", LibraryTypeKind.DispatchInterface, [], []));

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

    [TestMethod]
    public void GenerateReferenceUsings_WritesNamespaceAndEnumUsings()
    {
        var libA = MakeLibrary("ADODB",
            new LibraryTypeModel("CursorTypeEnum", LibraryTypeKind.Enum, [], [
                new("ForwardOnly", 0),
            ]),
            new LibraryTypeModel("Connection", LibraryTypeKind.DispatchInterface, [], []));

        var libB = MakeLibrary("MSComctlLib",
            new LibraryTypeModel("ListViewConstants", LibraryTypeKind.Enum, [], [
                new("lvwIcon", 0),
            ]),
            new LibraryTypeModel("ListView", LibraryTypeKind.DispatchInterface, [], []));

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
        var enumType = new LibraryTypeModel("Constants", LibraryTypeKind.Enum, [], [
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
            new LibraryTypeModel("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"),
            new LibraryTypeModel("OLE_HANDLE", LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"));
        var libB = MakeLibrary("OleAut32",
            new LibraryTypeModel("OLE_COLOR",  LibraryTypeKind.Alias, [], [], AliasedCSharpType: "uint"));

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

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────────
    // Struct (TKIND_RECORD) generation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_StructType_EmitsStructDeclaration()
    {
        var library = MakeLibrary("Win",
            new LibraryTypeModel("LOGPALETTE256", LibraryTypeKind.Struct,
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
            source.Should().Contain("GeneratedCode", "struct stubs must carry [GeneratedCode]");
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
            new LibraryTypeModel("POINT", LibraryTypeKind.Struct, [], []));

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

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static LibraryModel MakeLibrary(string safeName, params LibraryTypeModel[] types)
        => new(safeName, safeName, TestGuid, 1, 0, types, []);
}
