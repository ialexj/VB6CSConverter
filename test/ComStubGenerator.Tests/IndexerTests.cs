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
            // Should emit an indexer and a named method form, but NOT a plain property called "Fields"
            source.Should().Contain("this[");
            source.Should().Contain("Fields(", "named method form must be emitted for the default property");
            source.Should().NotContain("Fields {", "plain property must not be emitted");
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
            source.Should().Contain("Fields(", "named getter method must be emitted");
            source.Should().Contain("void SetFields(", "named setter method must be emitted");
            source.Should().NotContain("Fields {", "plain property must not be emitted");
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
        // The generated named method form should still preserve all optional defaults.
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
            source.Should().NotContain("this[dynamic Index = default", "the first optional default must be suppressed on the indexer");
            source.Should().Contain("short Dimension = default", "trailing optional defaults must be preserved");
            source.Should().Contain("int Flags = default", "trailing optional defaults must be preserved");
            source.Should().Contain("XArray(dynamic Index = default, short Dimension = default, int Flags = default)",
                "named method form must preserve optional defaults like regular methods");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_NonDefaultIndexedProperty_MethodPair_PreservesOptionalDefaults()
    {
        // Non-default parameterized properties emit method pairs, and should preserve
        // optional defaults exactly like regular methods.
        var library = MakeLibrary("XArrayLib",
            new ComQueryType("XArray", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object",
                        [
                            new("Index", "object", IsOptional: true, IsOut: false),
                            new("Dimension", "short", IsOptional: true, IsOut: false),
                            new("Flags", "int", IsOptional: true, IsOut: false),
                        ],
                        IsDefault: false),
                    new("Value", LibraryMemberKind.PropertySet, "void",
                        [
                            new("Index", "object", IsOptional: true, IsOut: false),
                            new("Dimension", "short", IsOptional: true, IsOut: false),
                            new("Flags", "int", IsOptional: true, IsOut: false),
                        ],
                        IsDefault: false),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("Value(dynamic Index = default, short Dimension = default, int Flags = default)",
                "getter method must preserve optional defaults");
            source.Should().Contain("SetValue(dynamic Index = default, short Dimension = default, int Flags = default, dynamic value)",
                "setter method must preserve optional defaults before the value parameter");
            source.Should().NotContain("this[", "non-default parameterized property must not become an indexer");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_IndexerAndDuplicateItemMethod_DispatchInterface_ItemMethodRenamedToGetItem()
    {
        // COM collection types (e.g. ComCtlLib.Panels) often define Item as both a PropertyGet
        // (DISPID 0, with params → C# indexer) and a Method with the same name.
        // C# forbids both because the indexer is internally named "Item" (CS0102),
        // so the explicit Item method should be renamed to GetItem.
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
            source.Should().Contain("Panel GetItem(", "the duplicate Item method must be renamed to GetItem");
            source.Should().NotContain("Panel Item(", "the original Item method signature must not remain");
            source.Should().Contain("short Count", "unrelated properties must still appear");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_IndexerAndDuplicateItemMethod_Class_ItemMethodRenamedToGetItem()
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
            source.Should().Contain("Panel GetItem(", "the duplicate Item method must be renamed to GetItem");
            source.Should().NotContain("Panel Item(", "the original Item method signature must not remain");
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
    public void Generate_ForwardingIndexerAndItemMethod_DispatchInterface_ItemMethodRenamedToGetItem()
    {
        // Forwarding indexer comes from Recordset.Fields -> Fields.Item(Index).
        // If Recordset also has a Method Item(Index), it should be renamed to GetItem.
        var library = MakeLibrary("DAO",
            new ComQueryType("Recordset", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Fields", LibraryMemberKind.PropertyGet, "DAO.Fields", [], IsDefault: true),
                    new("Item", LibraryMemberKind.Method, "DAO.Field",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: false),
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

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted for the two-hop DISPID 0 chain");
            recordsetSource.Should().Contain("DAO.Field GetItem(",
                "the Item method must be renamed to GetItem when an indexer is present");
            recordsetSource.Should().NotContain("DAO.Field Item(",
                "the original Item method signature must not remain");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Default property (non-Item name) → indexer + named method pair
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_DefaultPropertyNonItem_DispatchInterface_EmitsIndexerAndNamedMethods()
    {
        // XArrayObject.XArray.Value-style: default parameterized property that is NOT named "Item".
        // Must emit both a C# indexer (for arr[i] call sites) and a named method pair
        // (for VB6 call sites like xa.Value(i) and xa.Value(i) = v that survive conversion).
        var library = MakeLibrary("XArrayLib",
            new ComQueryType("XArray", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                    new("Value", LibraryMemberKind.PropertySet, "void",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "indexer must be emitted for bang-operator / index call sites");
            source.Should().Contain("Value(", "named getter method must be emitted");
            source.Should().Contain("void SetValue(", "named setter method must be emitted");
            source.Should().NotContain("Value {", "plain property must not be emitted");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyNonItem_Class_EmitsIndexerAndNamedMethods()
    {
        // Same as the DispatchInterface test but for a class type — methods must have throw bodies.
        var library = MakeLibrary("XArrayLib",
            new ComQueryType("XArray", LibraryTypeKind.Class,
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                    new("Value", LibraryMemberKind.PropertySet, "void",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "indexer must be emitted");
            source.Should().Contain("Value(", "named getter method must be emitted");
            source.Should().Contain("void SetValue(", "named setter method must be emitted");
            source.Should().Contain("NotImplementedException", "class stubs must have throw bodies");
            source.Should().NotContain("Value {", "plain property must not be emitted");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyNonItem_ReadOnly_DispatchInterface_EmitsIndexerAndGetterMethod()
    {
        // Read-only default parameterized property: indexer + getter method, no SetX.
        var library = MakeLibrary("XArrayLib",
            new ComQueryType("XArray", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "indexer must be emitted");
            source.Should().Contain("Value(", "named getter method must be emitted");
            source.Should().NotContain("SetValue", "no setter method should be emitted for a read-only property");
            source.Should().NotContain("Value {", "plain property must not be emitted");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyNamedItem_DispatchInterface_EmitsIndexerOnly()
    {
        // When the default parameterized property IS named "Item", only the indexer is emitted.
        // The method loop will handle any explicit Item method by renaming it to GetItem;
        // emitting a second GetItem from the property loop would create a duplicate.
        var library = MakeLibrary("TestLib",
            new ComQueryType("Collection", LibraryTypeKind.DispatchInterface,
                Members: [
                    new("Item", LibraryMemberKind.PropertyGet, "object",
                        [new("Index", "object", IsOptional: false, IsOut: false)],
                        IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("this[", "indexer must be emitted");
            source.Should().NotContain(" Item(", "no named method should be emitted for an Item default property");
            source.Should().NotContain("GetItem(", "no GetItem method should be auto-emitted for an Item default property");
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
