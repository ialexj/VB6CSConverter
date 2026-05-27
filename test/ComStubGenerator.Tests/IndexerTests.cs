using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class IndexerTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void Generate_DefaultPropertyWithParams_DispatchInterface_EmitsIndexer()
    {
        // DAO.Recordset-style: rs!MyField → rs["MyField"] needs this[string name] on the interface.
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "object",
                        [new("Name", "string", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                    new("EOF", LibraryMemberKind.PropertyGet, "bool", []),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            // Should emit an indexer, not a named property called "Fields"
            source.Should().Contain("this[");
            source.Should().NotContain("object Fields");
            // Regular non-default property should still appear as-is
            source.Should().Contain("bool EOF");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyWithParams_Class_EmitsIndexerWithThrowBody()
    {
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.Class,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "object",
                        [new("Name", "string", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                    new("Fields", LibraryMemberKind.PropertySet, "void",
                        [new("Name", "string", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[");
            source.Should().Contain("get");
            source.Should().Contain("set");
            source.Should().Contain("NotImplementedException");
            source.Should().NotContain("object Fields");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultIndexer_SkipsOnlyFirstOptionalDefault()
    {
        // XArray-style default member where all args are optional: keep trailing defaults,
        // but suppress only the first default value on the emitted indexer.
        var library = MakeLibrary("XArrayLib",
            new ComQueryType("XArrayObject", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("XArray", LibraryMemberKind.PropertyGet, "object",
                        [
                            new("Index", "object", IsOptional: true, IsOut: false),
                            new("Dimension", "short", IsOptional: true, IsOut: false),
                            new("Flags", "int", IsOptional: true, IsOut: false),
                        ],
                        IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "the default parameterized property must emit an indexer");
            source.Should().Contain("Index", "the first indexer parameter must be emitted");
            source.Should().NotContain("Index = default", "the first optional default must be suppressed");
            source.Should().Contain("short Dimension = default", "trailing optional defaults must be preserved");
            source.Should().Contain("int Flags = default", "trailing optional defaults must be preserved");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_IndexerAndDuplicateItemMethod_DispatchInterface_ItemMethodIsSkipped()
    {
        // COM collection types (e.g. ComCtlLib.Panels) often define Item as both a PropertyGet
        // (DISPID 0, with params → C# indexer) and a Method with the same name.
        // C# forbids both because the indexer is internally named "Item" (CS0102).
        var library = MakeLibrary("ComctlLib",
            new ComQueryType("Panels", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "ComctlLib.Panel",
                        [new("Index", "short", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                    new("Item", LibraryMemberKind.Method, "ComctlLib.Panel",
                        [new("Index", "short", IsOptional: false, IsOut: false)],
                        IsDefault: false),
                    new("Count", LibraryMemberKind.PropertyGet, "short", [], IsDefault: false),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "the indexer must be emitted");
            source.Should().NotContain("Panel Item(", "the duplicate Item method must be suppressed");
            source.Should().Contain("short Count", "unrelated properties must still appear");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_IndexerAndDuplicateItemMethod_Class_ItemMethodIsSkipped()
    {
        // Same as the DispatchInterface test but for a class type.
        var library = MakeLibrary("ComctlLib",
            new ComQueryType("Panels", LibraryTypeKind.Class,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "ComctlLib.Panel",
                        [new("Index", "short", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                    new("Item", LibraryMemberKind.Method, "ComctlLib.Panel",
                        [new("Index", "short", IsOptional: false, IsOut: false)],
                        IsDefault: false),
                    new("Count", LibraryMemberKind.PropertyGet, "short", [], IsDefault: false),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "the indexer must be emitted");
            source.Should().Contain("NotImplementedException", "class indexer must have a throw body");
            source.Should().NotContain("Panel Item(", "the duplicate Item method must be suppressed");
            source.Should().Contain("short Count", "unrelated properties must still appear");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyWithoutParams_EmitsRegularProperty()
    {
        // DISPID 0 with no parameters is a plain default value property — keep it as a named property.
        // When the return type is a primitive (not in the library), no forwarding indexer is emitted.
        var library = MakeLibrary("TestLib",
            new ComQueryType("Widget", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "string", [], IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("string Value");
            source.Should().NotContain("this[");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_EmitsForwardingIndexer()
    {
        // Models the DAO.Recordset / DAO.Fields pattern:
        //   Recordset.Fields (DISPID 0, no params) → Fields
        //   Fields.Item(object Index) (DISPID 0, with param) → Field
        // The outer type (Recordset) must get a this[object] forwarding indexer
        // so that rs["MyField"] compiles after the VB6 bang operator is lowered.
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "DAO.Fields", [], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Fields", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "DAO.Field",
                        [new("Index", "object", false, false)], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Field", LibraryTypeKind.DispatchInterface,
                Members: [],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("interface Recordset"));

            // Must still expose the named Fields property
            recordsetSource.Should().Contain("Fields",
                "the named Fields property must still be emitted");
            // Must also expose this[object] for rs["MyField"] to compile
            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted for the two-hop DISPID 0 chain");
            // The forwarding indexer's return type should match Fields.Item's return type
            recordsetSource.Should().Contain("DAO.Field",
                "forwarding indexer return type must match the inner collection's item type");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_ClassType_EmitsForwardingIndexer()
    {
        // Same two-hop pattern but for a class (not interface) outer type.
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.Class,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "DAO.Fields", [], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Fields", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "DAO.Field",
                        [new("Index", "object", false, false)], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Field", LibraryTypeKind.DispatchInterface,
                Members: [],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("class Recordset"));

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted for a class type too");
            recordsetSource.Should().Contain("DAO.Field",
                "forwarding indexer return type must match the inner collection's item type");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_WithSetter_EmitsReadWriteForwardingIndexer()
    {
        // When the inner collection's default member has both PropertyGet and PropertySet,
        // the forwarding indexer on the outer type must also expose a set accessor.
        // This is required for rs["MyField"] = value assignments to compile.
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "DAO.Fields", [], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Fields", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "DAO.Field",
                        [new("Index", "object", false, false)], IsDefault: true),
                    new("Item", LibraryMemberKind.PropertySet, "void",
                        [new("Index", "object", false, false)], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Field", LibraryTypeKind.DispatchInterface,
                Members: [],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("interface Recordset"));

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted");
            recordsetSource.Should().Contain("set",
                "the forwarding indexer must have a set accessor when the inner collection has a PropertySet");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_WithSetter_ClassType_EmitsReadWriteForwardingIndexer()
    {
        // Same read/write forwarding indexer test but for a class (not interface) outer type.
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.Class,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "DAO.Fields", [], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Fields", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "DAO.Field",
                        [new("Index", "object", false, false)], IsDefault: true),
                    new("Item", LibraryMemberKind.PropertySet, "void",
                        [new("Index", "object", false, false)], IsDefault: true),
                ],
                EnumValues: []),
            new ComQueryType("Field", LibraryTypeKind.DispatchInterface,
                Members: [],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("class Recordset"));

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted for a class type too");
            recordsetSource.Should().Contain("set",
                "the forwarding indexer must have a set accessor when the inner collection has a PropertySet");
            recordsetSource.Should().Contain("NotImplementedException",
                "class indexer must have throw bodies");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
