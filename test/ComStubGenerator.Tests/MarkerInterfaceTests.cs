using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class MarkerInterfaceTests : ReferenceStubGeneratorTestBase
{
    // ──────────────────────────────────────────────────────────────────────
    // GenerateMarkerInterfaces
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GenerateMarkerInterfaces_WritesFileAtReferenceRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var filePath = ReferenceStubGenerator.GenerateMarkerInterfaces(tempDir);

            filePath.Should().Be(Path.Combine(tempDir, "_ComStubInterfaces.cs"));
            File.Exists(filePath).Should().BeTrue();
            Path.GetDirectoryName(filePath).Should().Be(tempDir, "file must be at root, not in a subfolder");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateMarkerInterfaces_ContainsIComStubAndIOleStub()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var filePath = ReferenceStubGenerator.GenerateMarkerInterfaces(tempDir);
            var source = File.ReadAllText(filePath);

            source.Should().Contain("public interface IComStub", "IComStub must be declared");
            source.Should().Contain("public interface IOleStub", "IOleStub must be declared");
            source.Should().Contain("IOleStub : IComStub", "IOleStub must extend IComStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateMarkerInterfaces_ContainsIControlStub()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var filePath = ReferenceStubGenerator.GenerateMarkerInterfaces(tempDir);
            var source = File.ReadAllText(filePath);

            source.Should().Contain("public interface IControlStub<out T>", "IControlStub<T> must be covariant");
            source.Should().Contain("IComStub", "IControlStub<T> must extend IComStub");
            source.Should().Contain("where T : class", "IControlStub<T> must constrain T to class");
            source.Should().Contain("T Object =>", "IControlStub<T> must declare a default Object property");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Class stubs
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_ClassType_ImplementsIComStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyClass", LibraryTypeKind.Class));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("IComStub", "non-OLE class must implement IComStub");
            source.Should().NotContain("IOleStub", "non-OLE class must not implement IOleStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_OleObjectClass_ImplementsIOleStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyOleClass", LibraryTypeKind.Class, IsOleObject: true));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("IOleStub", "OLE object class must implement IOleStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ModuleType_DoesNotImplementMarkerInterface()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyModule", LibraryTypeKind.Module));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().NotContain("IComStub", "static module must not implement IComStub");
            source.Should().NotContain("IOleStub", "static module must not implement IOleStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Interface stubs
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_DispatchInterfaceType_ExtendsIComStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("IFoo", LibraryTypeKind.DispatchInterface,
                Members: [new("DoWork", LibraryMemberKind.Method, "void", [])],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("IComStub", "dispatch interface must extend IComStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_InterfaceType_ExtendsIComStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("IBar", LibraryTypeKind.Interface,
                Members: [new("GetValue", LibraryMemberKind.Method, "int", [])],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("IComStub", "interface must extend IComStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Struct stubs
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_StructType_ImplementsIComStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("POINT", LibraryTypeKind.Struct,
                Members: [
                    new("x", LibraryMemberKind.Field, "int", []),
                    new("y", LibraryMemberKind.Field, "int", []),
                ],
                EnumValues: []));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            var source = File.ReadAllText(written[0]);
            source.Should().Contain("IComStub", "struct must implement IComStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Existing base interfaces are preserved alongside marker
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_ClassWithExistingBaseInterfaces_MarkerPrependedToBaseList()
    {
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
            source.Should().Contain("IComStub", "marker must be in base list");
            source.Should().Contain("System.Collections.IEnumerable", "original base interface must still be present");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
