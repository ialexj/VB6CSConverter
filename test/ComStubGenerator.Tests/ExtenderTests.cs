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
}
