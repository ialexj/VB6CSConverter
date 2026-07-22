using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ConstFieldRewriterTests
{
    [TestMethod]
    public void PromotesNonConstantConstFieldToStaticReadonly()
    {
        CheckRewrites(
            """
            class T {
                public const string X = "a" + SomeIntConst;
                public const int SomeIntConst = 10;
            }
            """,
            """
            class T {
                public static readonly string X = "a" + SomeIntConst;
                public const int SomeIntConst = 10;
            }
            """);
    }

    [TestMethod]
    public void LeavesCompileTimeConstantFieldAsConst()
    {
        CheckRewrites(
            """
            class T {
                public const string X = "a" + SomeStringConst;
                public const string SomeStringConst = "b";
            }
            """);
    }

    private static void CheckRewrites(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new ConstFieldRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
