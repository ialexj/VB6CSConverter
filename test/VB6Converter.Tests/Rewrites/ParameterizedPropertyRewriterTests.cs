using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ParameterizedPropertyRewriterTests
{
    [TestMethod]
    public void Rewrites_MethodBacked_ElementAccessAssignment_To_SetMethod()
        => CheckRewrite(
            """
            class Combo
            {
                public object ItemData(int index) => null;
                public void SetItemData(int index, object value) { }
            }

            class Test
            {
                Combo cboTipo = new();

                void M()
                {
                    cboTipo.ItemData[0] = 42;
                }
            }
            """,
            """
            class Combo
            {
                public object ItemData(int index) => null;
                public void SetItemData(int index, object value) { }
            }

            class Test
            {
                Combo cboTipo = new();

                void M()
                {
                    cboTipo.SetItemData(0, 42);
                }
            }
            """);

    [TestMethod]
    public void DoesNotRewrite_ArrayField_ElementAccessAssignment()
        => CheckRewrite(
            """
            class Test
            {
                int[] items = new int[4];

                void M()
                {
                    items[0] = 42;
                }
            }
            """);

    [TestMethod]
    public void DoesNotRewrite_PropertyBacked_Array_ElementAccessAssignment()
        => CheckRewrite(
            """
            class Test
            {
                int[] Items { get; } = new int[4];

                void M()
                {
                    Items[0] = 42;
                }
            }
            """);

    [TestMethod]
    public void DoesNotRewrite_When_SetMethodIsMissing()
        => CheckRewrite(
            """
            class Combo
            {
                public object ItemData(int index) => null;
            }

            class Test
            {
                Combo cboTipo = new();

                void M()
                {
                    cboTipo.ItemData[0] = 42;
                }
            }
            """);

    private static void CheckRewrite(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create(
            "Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new ParameterizedPropertyRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        var actual = CSharpSyntaxTree.ParseText(newCu.ToFullString()).GetRoot().NormalizeWhitespace().ToFullString();
        var expectedText = CSharpSyntaxTree.ParseText(expected ?? cs).GetRoot().NormalizeWhitespace().ToFullString();
        actual.Should().Be(expectedText);
    }
}
