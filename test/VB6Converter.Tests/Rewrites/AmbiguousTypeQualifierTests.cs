using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class AmbiguousTypeQualifierTests
{
    // ── user-preferred namespace ──────────────────────────────────────────────

    [TestMethod]
    public void AmbiguousType_UserPreferred()
    {
        // Both A.Widget and B.Widget are in scope; user prefers B → B.Widget
        var cs = """
            using A;
            using B;
            namespace A { class Widget {} }
            namespace B { class Widget {} }
            class Test { Widget x; }
            """;

        var expected = """
            using A;
            using B;
            namespace A { class Widget {} }
            namespace B { class Widget {} }
            class Test { B.Widget x; }
            """;

        CheckQualification(cs, expected, preferredNamespaces: ["B"]);
    }

    [TestMethod]
    public void AmbiguousType_UserPreferred_FirstMatchWins()
    {
        // User lists both B and A; B is listed first → B.Widget
        var cs = """
            using A;
            using B;
            namespace A { class Widget {} }
            namespace B { class Widget {} }
            class Test { Widget x; }
            """;

        var expected = """
            using A;
            using B;
            namespace A { class Widget {} }
            namespace B { class Widget {} }
            class Test { B.Widget x; }
            """;

        CheckQualification(cs, expected, preferredNamespaces: ["B", "A"]);
    }

    // ── System.* fallback ─────────────────────────────────────────────────────

    [TestMethod]
    public void AmbiguousType_SystemPreferred()
    {
        // No user preferences; System.* candidate wins over App.*
        var cs = """
            using SystemLike;
            using App;
            namespace SystemLike { class Gadget {} }
            namespace App { class Gadget {} }
            class Test { Gadget x; }
            """;

        // SystemLike does NOT start with "System." so neither candidate is
        // System-preferred → first candidate wins.  Swap one to a real System prefix.
        var cs2 = """
            using System.Custom;
            using App;
            namespace System.Custom { class Gadget {} }
            namespace App { class Gadget {} }
            class Test { Gadget x; }
            """;

        var expected2 = """
            using System.Custom;
            using App;
            namespace System.Custom { class Gadget {} }
            namespace App { class Gadget {} }
            class Test { System.Custom.Gadget x; }
            """;

        CheckQualification(cs2, expected2, preferredNamespaces: []);
    }

    // ── first-candidate fallback ──────────────────────────────────────────────

    [TestMethod]
    public void AmbiguousType_NoPreference_FirstWins()
    {
        // No user preferences, no System.* candidates → first Roslyn candidate chosen.
        // We only verify the output is one of the two fully-qualified names.
        var cs = """
            using Alpha;
            using Beta;
            namespace Alpha { class Gadget {} }
            namespace Beta { class Gadget {} }
            class Test { Gadget x; }
            """;

        var result = RewriteCode(cs, preferredNamespaces: []);
        var isQualified = result.Contains("Alpha.Gadget") || result.Contains("Beta.Gadget");
        isQualified.Should().BeTrue("the unresolved simple name must be replaced by a fully-qualified name");
    }

    // ── no ambiguity ──────────────────────────────────────────────────────────

    [TestMethod]
    public void NoAmbiguity_Unchanged()
    {
        var cs = """
            using Alpha;
            namespace Alpha { class Widget {} }
            class Test { Widget x; }
            """;

        // No other Widget in scope → resolves cleanly, rewriter leaves it alone.
        CheckQualification(cs, cs, preferredNamespaces: []);
    }

    // ── multiple positions ────────────────────────────────────────────────────

    [TestMethod]
    public void AmbiguousInMultiplePositions()
    {
        // Ambiguous type appears in a field declaration, a method parameter, and a base type.
        var cs = """
            using A;
            using B;
            namespace A { class Token {} }
            namespace B { class Token {} }
            class Test : Token { Token field; void M(Token p) {} }
            """;

        var expected = """
            using A;
            using B;
            namespace A { class Token {} }
            namespace B { class Token {} }
            class Test : A.Token { A.Token field; void M(A.Token p) {} }
            """;

        CheckQualification(cs, expected, preferredNamespaces: ["A"]);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string RewriteCode(string cs, IEnumerable<string> preferredNamespaces)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new AmbiguousTypeQualifier(semantics, preferredNamespaces);
        return rewriter.Visit(cu).ToFullString();
    }

    private static void CheckQualification(string cs, string expected, IEnumerable<string> preferredNamespaces)
        => RewriteCode(cs, preferredNamespaces).Should().Be(expected);
}
