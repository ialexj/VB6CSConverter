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

    // ──────────────────────────────────────────────────────────────────────
    // Interface / class generation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_DispatchInterface_EmitsInterfaceWithMembers()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("Recordset", LibraryTypeKind.DispatchInterface,
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
            new ComQueryType("IAnimation", LibraryTypeKind.Interface,
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
            new ComQueryType("Widget", LibraryTypeKind.DispatchInterface,
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
            new ComQueryType("MathUtils", LibraryTypeKind.Module,
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
            new ComQueryType("Animation", LibraryTypeKind.Class,
                Members: [new("Play", LibraryMemberKind.Method, "void", [])],
                EnumValues: [],
                ImplementedInterfaces: ["IAnimation", "IDispatch"]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            // IDispatch is stripped from the base list by ComPlumbingFilterRewriter (default behaviour).
            source.Should().Contain("public class Animation : IAnimation");
            source.Should().NotContain("IDispatch");
            source.Should().Contain("NotImplementedException");
        }
        finally {
            if (Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Default property (DISPID 0) → C# indexer
    // ──────────────────────────────────────────────────────────────────────

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

    // ──────────────────────────────────────────────────────────────────────
    // Three-hop default-property chain (e.g. Recordset → Fields → Field → Value)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_ThreeHop_ResolvesTerminalType()
    {
        // Models the full DAO chain: rs!SomeColumn → rs.Fields["SomeColumn"].Value
        //   Recordset.Fields (DISPID 0, no params)      → Fields
        //   Fields.Item(object) (DISPID 0, with param)  → Field
        //   Field.Value (DISPID 0, no params)            → object
        // The forwarding indexer on Recordset must return dynamic (object), not DAO.Field.
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
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object", [], IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("interface Recordset"));

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted for the three-hop DISPID 0 chain");
            recordsetSource.Should().NotContain("DAO.Field this[",
                "the forwarding indexer must not use DAO.Field as its return type");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_ThreeHop_ClassType_ResolvesTerminalType()
    {
        // Same three-hop chain but for a class (not interface) outer type.
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
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object", [], IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("class Recordset"));

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted for a class type too");
            recordsetSource.Should().NotContain("DAO.Field this[",
                "the forwarding indexer must not use DAO.Field as its return type");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_DefaultPropertyForwardsThroughCollection_ThreeHop_WithSetter_EmitsReadWriteIndexer()
    {
        // When the terminal type's no-param default property has a setter (Field.Value { get; set; }),
        // the forwarding indexer on Recordset must also expose a set accessor.
        // COM property setters always carry the value-to-assign as their only parameter.
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
                Members: [
                    new("Value", LibraryMemberKind.PropertyGet, "object", [], IsDefault: true),
                    // COM emits the value-to-assign as the sole parameter of a plain PROPPUT
                    new("Value", LibraryMemberKind.PropertySet, "void",   [new("value", "object", false, false)], IsDefault: true),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var recordsetSource = written
                .Select(File.ReadAllText)
                .First(s => s.Contains("interface Recordset"));

            recordsetSource.Should().Contain("this[",
                "a forwarding indexer must be emitted");
            recordsetSource.Should().NotContain("DAO.Field this[",
                "the forwarding indexer must not return DAO.Field");
            recordsetSource.Should().Contain("set",
                "the forwarding indexer must have a set accessor when the terminal default property has a setter");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
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

    // ──────────────────────────────────────────────────────────────────────
    // Output path structure
    // ──────────────────────────────────────────────────────────────────────

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

    // ──────────────────────────────────────────────────────────────────────
    // IEnumerable (DISPID_NEWENUM / _NewEnum) stubs
    // ──────────────────────────────────────────────────────────────────────

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

    // ──────────────────────────────────────────────────────────────────────
    // Event collapsing (add_X / remove_X → event T X)
    // ──────────────────────────────────────────────────────────────────────

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

    // ──────────────────────────────────────────────────────────────────────
    // Method overloading
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_OverloadedMethodsEmittedWithSameName()
    {
        // CListWalker-style scenario: two interfaces contribute More() and More(object)
        var library = MakeLibrary("TestLib",
            new ComQueryType("CListWalker", LibraryTypeKind.Class,
                Members: [
                    new("More", LibraryMemberKind.Method, "bool", []),
                    new("More", LibraryMemberKind.Method, "bool", [new("v", "object", false, false)]),
                ],
                EnumValues: [],
                ImplementedInterfaces: ["_CListWalker", "IVariantWalker"]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().NotContain("More_2", "different signatures must be kept as overloads, not renamed");
            var moreCount = System.Text.RegularExpressions.Regex.Matches(source, @"\bMore\b").Count;
            moreCount.Should().Be(2, "both overloads of More must be emitted");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_Interface_OverloadedMethodsEmittedWithSameName()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("IWalker", LibraryTypeKind.Interface,
                Members: [
                    new("More", LibraryMemberKind.Method, "bool", []),
                    new("More", LibraryMemberKind.Method, "bool", [new("v", "object", false, false)]),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().NotContain("More_2", "different signatures must be kept as overloads, not renamed");
            var moreCount = System.Text.RegularExpressions.Regex.Matches(source, @"\bMore\b").Count;
            moreCount.Should().Be(2, "both overloads of More must be emitted");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_Class_TrueDuplicateMethodGetsRenamedSuffix()
    {
        // Identical signatures from two interfaces → second must be renamed
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyClass", LibraryTypeKind.Class,
                Members: [
                    new("More", LibraryMemberKind.Method, "bool", []),
                    new("More", LibraryMemberKind.Method, "bool", []),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            var source = File.ReadAllText(written[0]);
            source.Should().Contain("More_2", "a true duplicate (same signature) must still be renamed");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // IsControl extender property injection
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_ControlClass_InjectsExtenderProperties()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyCtrl", LibraryTypeKind.Class, IsControl: true));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            foreach (var name in new[] { "Left", "Top", "Width", "Height", "TabIndex",
                                         "_ExtentX", "_ExtentY", "_StockProps",
                                         "ToolTipText", "HelpContextID", "WhatsThisHelpID", "DragMode" }) {
                source.Should().Contain(name, $"extender property {name} should be injected");
            }

            // Each injected property must have both get and set accessors
            source.Should().Contain("get =>").And.Contain("set =>");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_NonControlClass_DoesNotInjectExtenderProperties()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyClass", LibraryTypeKind.Class));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            foreach (var name in new[] { "TabIndex", "_ExtentX", "_ExtentY", "_StockProps", "ToolTipText" }) {
                source.Should().NotContain(name, $"non-control class should not have extender property {name}");
            }
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ControlClass_DoesNotDuplicateExistingProperty()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyCtrl", LibraryTypeKind.Class,
                IsControl: true,
                Members: [
                    new ComQueryMember("Width", LibraryMemberKind.PropertyGet, "int", []),
                    new ComQueryMember("Width", LibraryMemberKind.PropertySet, "void", [
                        new ComQueryParam("value", "int", false, false)
                    ]),
                ]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            // "Width" should appear exactly once as a property declaration
            int count = 0;
            int pos = 0;
            while ((pos = source.IndexOf("int Width", pos, StringComparison.Ordinal)) >= 0) {
                count++;
                pos++;
            }
            count.Should().Be(1, "Width defined in the type library must not be duplicated by extender injection");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ControlModule_DoesNotInjectExtenderProperties()
    {
        // Modules generate static classes; extender injection is skipped for isStatic types.
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyModule", LibraryTypeKind.Module, IsControl: true));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            foreach (var name in new[] { "TabIndex", "_ExtentX", "_StockProps", "ToolTipText" }) {
                source.Should().NotContain(name, $"static module should not have extender property {name}");
            }
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static ComQueryLibrary MakeLibrary(string safeName, params ComQueryType[] types)
        => new(safeName, TestGuid, 1, 0, Types: types);

    // A library whose DiscoveredDependencies include mscorlib, triggering the
    // normalization + event-collapsing pipeline (DotnetLibraryGuids.RequiresNormalization).
    static readonly Guid MscorlibGuid = new("BED7F4EA-1A96-11d2-8F08-00A0C9A6186D");
    static ComQueryLibrary MakeDotnetLibrary(string safeName, params ComQueryType[] types)
        => new(safeName, TestGuid, 1, 0,
            Types: types,
            DiscoveredDependencies: [new ComQueryDiscoveredDep(MscorlibGuid, 2, 4)]);
}
