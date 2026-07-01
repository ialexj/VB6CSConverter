using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using System.Text.Json;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class FrxExpansionRewriterTests
{
    [TestMethod]
    public void ExpandsStringListAssignmentToSetCalls()
    {
        var tempRoot = CreateTempDirectory();
        try {
            var resourcesDir = Path.Combine(tempRoot, "_Resources");
            Directory.CreateDirectory(resourcesDir);

            var resourcePath = Path.Combine(resourcesDir, "Form1_0000.json");
            File.WriteAllText(resourcePath, JsonSerializer.Serialize(new[] { "Nenhuma", "Por Numero" }));

            var sourcePath = Path.Combine(tempRoot, "Form1.designer.cs");
            var cs = """
                class ComboLike
                {
                    public void SetList(short index, string value) { }
                }

                class Test
                {
                    ComboLike cboPeriodo = new();

                    void InitializeComponent()
                    {
                        cboPeriodo.List = default; // Resource: _Resources/Form1_0000.json
                    }
                }
                """;

            var newCu = Rewrite(cs, sourcePath);
            var output = newCu.ToFullString();

            output.Should().Contain("cboPeriodo.SetList(0,\"Nenhuma\")");
            output.Should().Contain("cboPeriodo.SetList(1,\"Por Numero\")");
            output.Should().NotContain("cboPeriodo.List = default");
            output.Should().NotContain("Resource: _Resources/Form1_0000.json");
        }
        finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [TestMethod]
    public void DoesNotRewriteWhenSetterMissing()
    {
        var tempRoot = CreateTempDirectory();
        try {
            var resourcesDir = Path.Combine(tempRoot, "_Resources");
            Directory.CreateDirectory(resourcesDir);

            var resourcePath = Path.Combine(resourcesDir, "Form1_0000.json");
            File.WriteAllText(resourcePath, JsonSerializer.Serialize(new[] { "A" }));

            var sourcePath = Path.Combine(tempRoot, "Form1.designer.cs");
            var cs = """
                class ComboLike
                {
                    public string List { get; set; }
                }

                class Test
                {
                    ComboLike cboPeriodo = new();

                    void InitializeComponent()
                    {
                        cboPeriodo.List = default; // Resource: _Resources/Form1_0000.json
                    }
                }
                """;

            var newCu = Rewrite(cs, sourcePath);
            var output = newCu.ToFullString();

            output.Should().Contain("cboPeriodo.List = default");
            output.Should().NotContain("SetList(");
        }
        finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [TestMethod]
    public void ResolvesResourcePathByWalkingUpDirectories()
    {
        var tempRoot = CreateTempDirectory();
        try {
            var resourcesDir = Path.Combine(tempRoot, "_Resources");
            var nestedDir = Path.Combine(tempRoot, "Forms", "Nested");
            Directory.CreateDirectory(resourcesDir);
            Directory.CreateDirectory(nestedDir);

            var resourcePath = Path.Combine(resourcesDir, "Form1_0000.json");
            File.WriteAllText(resourcePath, JsonSerializer.Serialize(new[] { "X" }));

            var sourcePath = Path.Combine(nestedDir, "Form1.designer.cs");
            var cs = """
                class ComboLike
                {
                    public void SetList(int index, string value) { }
                }

                class Test
                {
                    ComboLike cboPeriodo = new();

                    void InitializeComponent()
                    {
                        cboPeriodo.List = default; // Resource: _Resources/Form1_0000.json
                    }
                }
                """;

            var newCu = Rewrite(cs, sourcePath);
            var output = newCu.ToFullString();

            output.Should().Contain("cboPeriodo.SetList(0,\"X\")");
        }
        finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static CompilationUnitSyntax Rewrite(string code, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(code, path: path);
        var cu = (CompilationUnitSyntax)tree.GetRoot();

        var comp = CSharpCompilation.Create(
            "Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(tree, true);
        return (CompilationUnitSyntax)new FrxExpansionRewriter(semantics).Visit(cu);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vb6converter-frx-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
