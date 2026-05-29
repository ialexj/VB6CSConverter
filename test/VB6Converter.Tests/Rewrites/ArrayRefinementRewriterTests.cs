using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Threading.Tasks;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ArrayRefinementRewriterTests
{
    // ── Both rank and element type refined ────────────────────────────────────

    [TestMethod]
    public Task RankAndTypeBothRefined() => CheckRefinement(
        """
        string[] x = default;
        x = new dynamic[0, 0];
        """,
        """
        string[,] x = default;
        x = new string[0, 0];
        """);

    // ── Only element type refined (rank already matches) ──────────────────────

    [TestMethod]
    public Task OnlyElementTypeRefined() => CheckRefinement(
        """
        string[] x = default;
        x = new dynamic[5];
        """,
        """
        string[] x = default;
        x = new string[5];
        """);

    // ── No change: all types are dynamic — nothing specific to refine to ──────

    [TestMethod]
    public Task AllDynamic_NoChange() => CheckNoChange(
        """
        dynamic[] x = default;
        x = new dynamic[0];
        """);

    // ── No change: conflicting specific types (string vs int) ─────────────────

    [TestMethod]
    public Task TypeConflict_NoChange() => CheckNoChange(
        """
        string[] x = default;
        x = new int[0];
        """);

    // ── No change: assignments disagree on rank ────────────────────────────────

    [TestMethod]
    public Task RankDisagreement_NoChange() => CheckNoChange(
        """
        dynamic[] x = default;
        x = new dynamic[0];
        x = new dynamic[0, 0];
        """);

    // ── Multiple assignments that agree → both are updated ────────────────────

    [TestMethod]
    public Task MultipleAssignmentsAgree() => CheckRefinement(
        """
        dynamic[] x = default;
        x = new string[0, 0];
        x = new string[1, 2];
        """,
        """
        string[,] x = default;
        x = new string[0, 0];
        x = new string[1, 2];
        """);

    // ── Field in a class is refined ───────────────────────────────────────────

    [TestMethod]
    public Task FieldRefined() => CheckRefinement(
        """
        class TestClass
        {
            string[] arr = default;
            void Test()
            {
                arr = new dynamic[0, 0];
            }
        }
        """,
        """
        class TestClass
        {
            string[,] arr = default;
            void Test()
            {
                arr = new string[0, 0];
            }
        }
        """);

    // ── No change: RHS is not an array-creation expression ────────────────────

    [TestMethod]
    public Task NonArrayCreationAssignment_NoChange() => CheckNoChange(
        """
        string[] x = default;
        string[] y = new string[5];
        x = y;
        """);

    // ── Already correct (specific declared type, matching creation) — no rewrite

    [TestMethod]
    public Task AlreadyCorrect_NoChange() => CheckNoChange(
        """
        string[,] x = default;
        x = new string[0, 0];
        """);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Task CheckNoChange(string cs) => CheckRefinement(cs, cs);

    private static async Task CheckRefinement(string cs, string expected)
    {
        using var workspace = new AdhocWorkspace();

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "TestProject",
            "TestProject",
            LanguageNames.CSharp)
            .WithMetadataReferences([
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ])
            .WithCompilationOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var project = workspace.AddProject(projectInfo);
        var document = project.AddDocument("Test.cs", cs);
        var semantics = await document.GetSemanticModelAsync();
        var solution = document.Project.Solution;
        var cu = semantics!.SyntaxTree.GetCompilationUnitRoot();

        var declaratorTypes = new Dictionary<VariableDeclaratorSyntax, ArrayTypeSyntax>();
        var symbolTypes = new Dictionary<ISymbol, ArrayTypeSyntax>(SymbolEqualityComparer.Default);
        await ArrayRefinementRewriter.GetAllArrayVariablesAndUsages(semantics!, solution, declaratorTypes, symbolTypes);

        var rewriter = new ArrayRefinementRewriter(semantics!, declaratorTypes, symbolTypes);
        var newCu = (CompilationUnitSyntax)rewriter.Visit(cu)!;

        newCu.NormalizeWhitespace().ToFullString()
            .Should().Be(SyntaxFactory.ParseCompilationUnit(expected).NormalizeWhitespace().ToFullString());
    }
}
