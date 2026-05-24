using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class TypeDeclarationTests : ReferenceStubGeneratorTestBase
{
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
}
