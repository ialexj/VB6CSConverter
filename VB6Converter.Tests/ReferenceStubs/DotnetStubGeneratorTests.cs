using AwesomeAssertions;
using VB6Converter.ReferenceStubs;

namespace VB6Converter.Tests.ReferenceStubs;

/// <summary>
/// Tests that the stub generator correctly rewrites mscorlib type references to their
/// canonical .NET equivalents, since mscorlib is excluded from harvesting.
/// </summary>
[TestClass]
public class DotnetStubGeneratorTests
{
    static readonly Guid TestGuid = new("12345678-0000-0000-0000-000000000099");

    // ──────────────────────────────────────────────────────────────────────
    // _Object — should be omitted from base list entirely
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_ObjectInBaseList_IsOmitted()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("Widget", LibraryTypeKind.Class,
                Members: [new("Name", LibraryMemberKind.PropertyGet, "string", [])],
                EnumValues: [],
                ImplementedInterfaces: ["_Object", "IWidget"]));

        var source = GenerateSingle(library);

        source.Should().NotContain("_Object", "the mscorlib _Object interface must be dropped from the base list");
        source.Should().Contain("IWidget", "non-mscorlib interfaces must still appear");
    }

    [TestMethod]
    public void Generate_Class_QualifiedMscorlibObjectInBaseList_IsOmitted()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("Widget", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["mscorlib._Object"]));

        var source = GenerateSingle(library);

        source.Should().NotContain("_Object");
        source.Should().NotContain("mscorlib");
    }

    [TestMethod]
    public void Generate_Class_OnlyObjectInBaseList_ProducesNoBaseList()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("Widget", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["_Object"]));

        var source = GenerateSingle(library);

        // No colon after the class name means no base list
        source.Should().NotContain("Widget :");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Collection interfaces — must be qualified with System.Collections
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_IEnumerableInBaseList_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyList", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["IEnumerable"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Collections.IEnumerable");
        source.Should().NotContain(": IEnumerable", "bare IEnumerable must not appear");
    }

    [TestMethod]
    public void Generate_Class_ICollectionInBaseList_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyList", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["ICollection"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Collections.ICollection");
    }

    [TestMethod]
    public void Generate_Class_IListInBaseList_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyList", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["IList"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Collections.IList");
    }

    [TestMethod]
    public void Generate_Interface_QualifiedMscorlibIEnumerableInBaseList_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("IMyCollection", LibraryTypeKind.Interface,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["mscorlib.IEnumerable"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Collections.IEnumerable");
        source.Should().NotContain("mscorlib");
    }

    // ──────────────────────────────────────────────────────────────────────
    // ISerializable — must be qualified with System.Runtime.Serialization
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_ISerializableInBaseList_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyObject", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["ISerializable"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Runtime.Serialization.ISerializable");
        source.Should().NotContain(": ISerializable");
    }

    [TestMethod]
    public void Generate_Class_QualifiedMscorlibISerializableInBaseList_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyObject", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["mscorlib.ISerializable"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Runtime.Serialization.ISerializable");
        source.Should().NotContain("mscorlib");
    }

    // ──────────────────────────────────────────────────────────────────────
    // mscorlib._Type and mscorlib._Array — must map to Type / Array in member types
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_MscorlibTypeReturnType_MapsToType()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("TypeHelper", LibraryTypeKind.Class,
                Members: [new("GetTypeInfo", LibraryMemberKind.Method, "mscorlib._Type", [])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("Type GetTypeInfo(");
        source.Should().NotContain("mscorlib._Type");
    }

    [TestMethod]
    public void Generate_Class_MscorlibArrayReturnType_MapsToArray()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("ArrayHelper", LibraryTypeKind.Class,
                Members: [new("GetItems", LibraryMemberKind.Method, "mscorlib._Array", [])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("Array GetItems(");
        source.Should().NotContain("mscorlib._Array");
    }

    [TestMethod]
    public void Generate_Interface_MscorlibTypeReturnType_MapsToType()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("ITypeProvider", LibraryTypeKind.Interface,
                Members: [new("GetType", LibraryMemberKind.Method, "mscorlib._Type", [])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("Type GetType(");
        source.Should().NotContain("mscorlib._Type");
    }

    [TestMethod]
    public void Generate_Class_MscorlibTypePropertyType_MapsToType()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("TypeHolder", LibraryTypeKind.Class,
                Members: [new("TypeInfo", LibraryMemberKind.PropertyGet, "mscorlib._Type", [])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("Type TypeInfo");
        source.Should().NotContain("mscorlib._Type");
    }

    [TestMethod]
    public void Generate_Class_MscorlibIEnumerableParamType_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("Sorter", LibraryTypeKind.Class,
                Members: [new("Sort", LibraryMemberKind.Method, "void", [
                    new("items", "mscorlib.IEnumerable", IsOptional: false, IsOut: false),
                ])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Collections.IEnumerable items");
        source.Should().NotContain("mscorlib.IEnumerable");
    }

    [TestMethod]
    public void Generate_Struct_MscorlibTypeFieldType_MapsToType()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("TypeDesc", LibraryTypeKind.Struct,
                Members: [new("TypeRef", LibraryMemberKind.Field, "mscorlib._Type", [])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("public System.Type TypeRef");
        source.Should().NotContain("mscorlib._Type");
    }

    // ──────────────────────────────────────────────────────────────────────
    // mscorlib._Exception, _EventHandler, _SerializationInfo, _StreamingContext
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_MscorlibExceptionInBaseList_MapsToSystemException()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyError", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["_Exception"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Exception");
        source.Should().NotContain("_Exception");
    }

    [TestMethod]
    public void Generate_Class_ExceptionNotFirstInBaseList_MovedToFirst()
    {
        // COM type libraries can list _Exception after other interfaces;
        // the rewriter must promote it to position 0 (C# requires base class first).
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyError", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["ISerializable", "_Exception", "IDisposable"]));

        var source = GenerateSingle(library);

        var colonIdx = source.IndexOf(':');
        var exceptionIdx = source.IndexOf("System.Exception", colonIdx);
        var serializableIdx = source.IndexOf("ISerializable", colonIdx);
        var disposableIdx = source.IndexOf("IDisposable", colonIdx);

        exceptionIdx.Should().BeLessThan(serializableIdx, "System.Exception must appear before ISerializable");
        exceptionIdx.Should().BeLessThan(disposableIdx, "System.Exception must appear before IDisposable");
    }

    [TestMethod]
    public void Generate_Class_QualifiedMscorlibExceptionReturnType_MapsToSystemException()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("ErrorFactory", LibraryTypeKind.Class,
                Members: [new("Create", LibraryMemberKind.Method, "mscorlib._Exception", [])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Exception Create(");
        source.Should().NotContain("mscorlib._Exception");
    }

    [TestMethod]
    public void Generate_Class_MscorlibEventHandlerInBaseList_MapsToSystemEventHandler()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("MyDelegate", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["_EventHandler"]));

        var source = GenerateSingle(library);

        source.Should().Contain("System.EventHandler");
        source.Should().NotContain("_EventHandler");
    }

    [TestMethod]
    public void Generate_Class_QualifiedMscorlibEventHandlerParamType_MapsToSystemEventHandler()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("EventSource", LibraryTypeKind.Class,
                Members: [new("Subscribe", LibraryMemberKind.Method, "void", [
                    new("handler", "mscorlib._EventHandler", IsOptional: false, IsOut: false),
                ])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("System.EventHandler handler");
        source.Should().NotContain("mscorlib._EventHandler");
    }

    [TestMethod]
    public void Generate_Class_MscorlibSerializationInfoParamType_IsQualified()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("Serializable", LibraryTypeKind.Class,
                Members: [new("GetObjectData", LibraryMemberKind.Method, "void", [
                    new("info",    "mscorlib._SerializationInfo",  IsOptional: false, IsOut: false),
                    new("context", "mscorlib._StreamingContext",   IsOptional: false, IsOut: false),
                ])],
                EnumValues: []));

        var source = GenerateSingle(library);

        source.Should().Contain("System.Runtime.Serialization.SerializationInfo info");
        source.Should().Contain("System.Runtime.Serialization.StreamingContext context");
        source.Should().NotContain("mscorlib._SerializationInfo");
        source.Should().NotContain("mscorlib._StreamingContext");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Unrelated interfaces are not affected
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_Class_UnrelatedInterfaces_PassThrough()
    {
        var library = MakeLibrary("SomeLib",
            new LibraryTypeModel("Widget", LibraryTypeKind.Class,
                Members: [],
                EnumValues: [],
                ImplementedInterfaces: ["IWidget", "IDispatch"]));

        var source = GenerateSingle(library);

        source.Should().Contain("IWidget");
        source.Should().Contain("IDispatch");
    }

    // ──────────────────────────────────────────────────────────────────────
    // No mscorlib dependency → normalization is skipped entirely
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_NoDotnetDeps_MscorlibTypeNamesAreNotNormalized()
    {
        // A library with no .NET runtime in its DiscoveredDependencies should NOT
        // have mscorlib type references rewritten.
        var library = MakeLibraryNoDotnetDeps("SomeLib",
            new LibraryTypeModel("Widget", LibraryTypeKind.Interface,
                Members: [new("GetEnum", LibraryMemberKind.Method, "mscorlib.IEnumerable", [])],
                EnumValues: [],
                ImplementedInterfaces: ["mscorlib.IEnumerable"]));

        var source = GenerateSingle(library);

        source.Should().Contain("mscorlib", "type names are left as-is when the library has no .NET runtime dependency");
        source.Should().NotContain("System.Collections.IEnumerable");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static string GenerateSingle(LibraryModel library)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            written.Should().ContainSingle();
            return File.ReadAllText(written[0]);
        }
        finally {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // mscorlib.tlb GUID — hardcoded so the rewriter is triggered in tests
    // (matches DotnetLibraryGuids.Mscorlib in the production code).
    static readonly Guid MscorlibGuid = new("BED7F4EA-1A96-11d2-8F08-00A0C9A6186D");

    /// <summary>Library that depends on mscorlib — normalization rewriter is applied.</summary>
    static LibraryModel MakeLibrary(string safeName, params LibraryTypeModel[] types)
        => new(safeName, safeName, TestGuid, 1, 0, types,
               [new DiscoveredDependency(MscorlibGuid, 2, 0)]);

    /// <summary>Library with no .NET runtime dependency — normalization rewriter is NOT applied.</summary>
    static LibraryModel MakeLibraryNoDotnetDeps(string safeName, params LibraryTypeModel[] types)
        => new(safeName, safeName, TestGuid, 1, 0, types, []);
}
