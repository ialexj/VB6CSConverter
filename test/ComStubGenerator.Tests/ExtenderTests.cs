using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class ExtenderTests : ReferenceStubGeneratorTestBase
{
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
    // IControlStub<T> base interface
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Generate_ControlClass_ImplementsIControlStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyCtrl", LibraryTypeKind.Class, IsControl: true));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            source.Should().Contain("IControlStub<MyCtrl>",
                "control class must implement IControlStub<TSelf>");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_NonControlClass_DoesNotImplementIControlStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyClass", LibraryTypeKind.Class));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            source.Should().NotContain("IControlStub",
                "non-control class must not implement IControlStub");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_OleControlClass_ImplementsBothIOleStubAndIControlStub()
    {
        var library = MakeLibrary("TestLib",
            new ComQueryType("MyOleCtrl", LibraryTypeKind.Class,
                IsControl: true, IsOleObject: true));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var source = File.ReadAllText(written[0]);

            source.Should().Contain("IOleStub",
                "OLE control class must implement IOleStub");
            source.Should().Contain("IControlStub<MyOleCtrl>",
                "OLE control class must also implement IControlStub<TSelf>");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ExtenderClass_GeneratesExtensionFile()
    {
        var library = MakeLibrary("VB",
            new ComQueryType("VBControlExtender", LibraryTypeKind.Class,
                IsControl: true,
                Members: [
                    new ComQueryMember("Caption", LibraryMemberKind.PropertyGet, "string", []),
                    new ComQueryMember("Caption", LibraryMemberKind.PropertySet, "void", [
                        new ComQueryParam("value", "string", false, false),
                    ]),
                    new ComQueryMember("Refresh", LibraryMemberKind.Method, "void", []),
                ]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().Contain(p => p.EndsWith("VBControlExtender.cs", StringComparison.Ordinal));
            written.Should().Contain(p => p.EndsWith("VBControlExtenderExtensions.cs", StringComparison.Ordinal));
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ExtenderClass_ExtensionTargetsIComStub()
    {
        var library = MakeLibrary("VB",
            new ComQueryType("VBControlExtender", LibraryTypeKind.Class,
                Members: [
                    new ComQueryMember("DoWork", LibraryMemberKind.Method, "void", []),
                ]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var extensionPath = written.First(p => p.EndsWith("VBControlExtenderExtensions.cs", StringComparison.Ordinal));
            var source = File.ReadAllText(extensionPath);

            source.Should().Contain("extension(IComStub self)");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_ExtenderClass_ExtensionContainsAllMemberKinds()
    {
        var library = MakeLibrary("VB",
            new ComQueryType("VBControlExtender", LibraryTypeKind.Class,
                IsControl: true,
                Members: [
                    new ComQueryMember("Caption", LibraryMemberKind.PropertyGet, "string", []),
                    new ComQueryMember("Caption", LibraryMemberKind.PropertySet, "void", [
                        new ComQueryParam("value", "string", false, false),
                    ]),
                    new ComQueryMember("ToolTipText", LibraryMemberKind.PropertyGet, "string", []),
                    new ComQueryMember("ToolTipText", LibraryMemberKind.PropertySet, "void", [
                        new ComQueryParam("value", "string", false, false),
                    ]),
                    new ComQueryMember("Move", LibraryMemberKind.Method, "void", [
                        new ComQueryParam("x", "int", false, false),
                        new ComQueryParam("y", "int", false, false),
                    ]),
                    new ComQueryMember("Item", LibraryMemberKind.PropertyGet, "object", [
                        new ComQueryParam("index", "object", false, false),
                    ], IsDefault: true),
                ]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);
            var extensionPath = written.First(p => p.EndsWith("VBControlExtenderExtensions.cs", StringComparison.Ordinal));
            var source = File.ReadAllText(extensionPath);

            source.Should().Contain("public string Caption");
            source.Should().Contain("public void Move(");
            source.Should().Contain("public dynamic this[dynamic index]");
            source.Should().Contain("public int Left", "injected extender property should be present on extension block as well");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Generate_NonExtenderClass_DoesNotGenerateExtensionFile()
    {
        var library = MakeLibrary("VB",
            new ComQueryType("VBControl", LibraryTypeKind.Class,
                Members: [
                    new ComQueryMember("DoWork", LibraryMemberKind.Method, "void", []),
                ]));

        var tempDir = Path.Combine(Path.GetTempPath(), $"stubs_{Guid.NewGuid():N}");
        try {
            var written = ReferenceStubGenerator.Generate(library, tempDir);

            written.Should().ContainSingle();
            written[0].Should().EndWith("VBControl.cs");
        }
        finally {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
