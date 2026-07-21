using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class TypeCastRewriterTests
{
    private const string EnumDeclarations =
        """
        namespace TrueDBGrid50 {
            public enum enumMarqueeStyleConstants { _DottedCellBorder = 0, _SolidCellBorder = 1 }
        }
        public enum MarqueeStyleConstants { dbgDottedCellBorder = 0, dbgSolidCellBorder = 1 }
        """;

    [TestMethod]
    public void CastsEnumToEnumByValueInAssignment_BareIdentifier()
        => CheckRewrites(
            "using static MarqueeStyleConstants;\n" + EnumDeclarations +
            """
            class T {
                TrueDBGrid50.enumMarqueeStyleConstants MarqueeStyle { get; set; }
                void M() {
                    this.MarqueeStyle = dbgSolidCellBorder;
                }
            }
            """,
            "using static MarqueeStyleConstants;\n" + EnumDeclarations +
            """
            class T {
                TrueDBGrid50.enumMarqueeStyleConstants MarqueeStyle { get; set; }
                void M() {
                    this.MarqueeStyle = (TrueDBGrid50.enumMarqueeStyleConstants)dbgSolidCellBorder;
                }
            }
            """);

    [TestMethod]
    public void CastsEnumToEnumByValueInAssignment_MemberAccess()
        => CheckRewrites(
            EnumDeclarations +
            """
            class T {
                TrueDBGrid50.enumMarqueeStyleConstants MarqueeStyle { get; set; }
                void M() { this.MarqueeStyle = MarqueeStyleConstants.dbgSolidCellBorder; }
            }
            """,
            EnumDeclarations +
            """
            class T {
                TrueDBGrid50.enumMarqueeStyleConstants MarqueeStyle { get; set; }
                void M() { this.MarqueeStyle = (TrueDBGrid50.enumMarqueeStyleConstants)MarqueeStyleConstants.dbgSolidCellBorder; }
            }
            """);

    [TestMethod]
    public void LeavesAssignmentUnchanged_WhenNoMatchingValueExists()
        => CheckRewrites(
            """
            enum A { One = 1, Two = 2 }
            enum B { Three = 3 }
            class T {
                A Value { get; set; }
                void M() { this.Value = B.Three; }
            }
            """);

    [TestMethod]
    public void LeavesAssignmentUnchanged_WhenSameEnumType()
        => CheckRewrites(
            """
            enum A { One = 1 }
            class T {
                A Value { get; set; }
                void M() { this.Value = A.One; }
            }
            """);

    [TestMethod]
    public void LeavesAssignmentUnchanged_WhenAlreadyExplicitlyCast()
        => CheckRewrites(
            """
            enum A { One = 1 }
            enum B { One = 1 }
            class T {
                A Value { get; set; }
                void M() { this.Value = (A)B.One; }
            }
            """);

    [TestMethod]
    public void LeavesAssignmentUnchanged_WhenRhsIsNotCompileTimeConstant()
        => CheckRewrites(
            """
            enum A { One = 1 }
            enum B { One = 1 }
            class T {
                A Value { get; set; }
                void M(B b) { this.Value = b; }
            }
            """);

    [TestMethod]
    public void CastsEnumToEnumByValueInArgument()
        => CheckRewrites(
            """
            enum A { One = 1 }
            enum B { One = 1 }
            class T {
                void Take(A value) { }
                void M() { Take(B.One); }
            }
            """,
            """
            enum A { One = 1 }
            enum B { One = 1 }
            class T {
                void Take(A value) { }
                void M() { Take((A)B.One); }
            }
            """);

    [TestMethod]
    public void CastsEnumToEnumByValueInBinaryComparison()
        => CheckRewrites(
            """
            enum A { One = 1 }
            enum B { One = 1 }
            class T {
                void M(A a) { var x = a == B.One; }
            }
            """,
            """
            enum A { One = 1 }
            enum B { One = 1 }
            class T {
                void M(A a) { var x = a == (A)B.One; }
            }
            """);

    private static void CheckRewrites(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new TypeCastRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
