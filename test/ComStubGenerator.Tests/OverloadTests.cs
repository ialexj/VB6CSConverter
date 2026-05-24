using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class OverloadTests : ReferenceStubGeneratorTestBase
{
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
}
