using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using VB6Converter.Rewriters;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class LargeFileSplitterTests
{
    /// <summary>Builds a CompilationUnit with <paramref name="methodCount"/> methods of approximately
    /// <paramref name="linesEach"/> lines each, wrapped in a partial class and file-scoped namespace.</summary>
    static CompilationUnitSyntax BuildSyntheticCU(int methodCount, int linesEach = 10)
    {
        var sb = new StringBuilder();
        sb.AppendLine("namespace TestNs;");
        sb.AppendLine("[System.CodeDom.Compiler.GeneratedCode(\"VB6Converter\", \"1.0\")]");
        sb.AppendLine("public partial class TestClass");
        sb.AppendLine("{");
        for (int i = 0; i < methodCount; i++) {
            sb.AppendLine($"    public void Method{i}()");
            sb.AppendLine("    {");
            for (int j = 0; j < linesEach - 3; j++) {
                sb.AppendLine($"        // line {j}");
            }
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");

        return CSharpSyntaxTree.ParseText(sb.ToString()).GetCompilationUnitRoot();
    }

    [TestMethod]
    public void SmallFile_ReturnsSingleChunk()
    {
        var cu = BuildSyntheticCU(methodCount: 3, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 5000);

        result.Should().HaveCount(1);
        result[0].Should().BeSameAs(cu);
    }

    [TestMethod]
    public void LargeFile_SplitsIntoMultipleChunks()
    {
        // 20 methods × 10 lines = ~200 lines; split at 50 → should produce several chunks
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        result.Count.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void AllMethodsPreservedAcrossChunks()
    {
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        var allMethods = result
            .SelectMany(r => r.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Select(m => m.Identifier.Text)
            .OrderBy(x => x)
            .ToList();

        var expected = Enumerable.Range(0, 20)
            .Select(i => $"Method{i}")
            .OrderBy(x => x)
            .ToList();

        allMethods.Should().Equal(expected);
    }

    [TestMethod]
    public void EachChunk_HasSameClassName()
    {
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        foreach (var chunk in result) {
            chunk.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Should().ContainSingle()
                .Which.Identifier.Text.Should().Be("TestClass");
        }
    }

    [TestMethod]
    public void EachChunk_IsPartial()
    {
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        foreach (var chunk in result) {
            chunk.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Should().ContainSingle()
                .Which.Modifiers.Should().Contain(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
        }
    }

    [TestMethod]
    public void EachChunk_HasGeneratedCodeAttribute()
    {
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        foreach (var chunk in result) {
            var cls = chunk.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            cls.AttributeLists
                .SelectMany(al => al.Attributes)
                .Should().Contain(a => a.Name.ToString().Contains("GeneratedCode"));
        }
    }

    [TestMethod]
    public void EachChunk_HasSameNamespace()
    {
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        foreach (var chunk in result) {
            chunk.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>()
                .Should().ContainSingle()
                .Which.Name.ToString().Should().Be("TestNs");
        }
    }

    [TestMethod]
    public void SubsequentChunks_HaveNoBaseList()
    {
        var cu = BuildSyntheticCU(methodCount: 20, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        // chunks beyond the first must not carry a base list
        foreach (var chunk in result.Skip(1)) {
            chunk.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Single().BaseList.Should().BeNull();
        }
    }

    [TestMethod]
    public void MaxLinesZero_NotCalledFromNormalPath_NoOp()
    {
        // LargeFileSplitter doesn't check maxLines <= 0 itself;
        // callers guard with `if (options.SplitLines > 0)`.
        // Verify that a 0 maxLines causes every method to be its own chunk
        // (degenerate case — every flush threshold is immediately exceeded).
        var cu = BuildSyntheticCU(methodCount: 3, linesEach: 10);
        var result = LargeFileSplitter.Split(cu, maxLines: 0);

        // 3 methods with 10 lines each → totalLines ~33, which exceeds 0.
        // Each method flushes its own chunk (3 chunks).
        result.Count.Should().Be(3);
    }

    [TestMethod]
    public void SingleGiantMethod_CannotSplit_ReturnsSingleChunk()
    {
        // A single method of 200 lines cannot be split at member boundaries
        var cu = BuildSyntheticCU(methodCount: 1, linesEach: 200);
        var result = LargeFileSplitter.Split(cu, maxLines: 50);

        result.Should().HaveCount(1);
    }
}
