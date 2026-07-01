using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class DefaultMemberRewriterTests
{
    private const string CheckBoxDecl =
        "using System.Reflection; " +
        "[DefaultMember(\"_Default\")] class CheckBox { public short _Default { get; set; } } " +
        "enum CheckBoxConstants : short { VbUnchecked = 0, VbChecked = 1 } ";

    [TestMethod]
    public void ExpandsDefaultMember_When_AssignedFromEnum()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { chkArtigos = CheckBoxConstants.VbChecked; } }",
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { chkArtigos._Default = CheckBoxConstants.VbChecked; } }");

    [TestMethod]
    public void ExpandsDefaultMember_When_AssignedToExistingNumericVariable()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { short test = 0; test = chkArtigos; } }",
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { short test = 0; test = chkArtigos._Default; } }");

    [TestMethod]
    public void ExpandsDefaultMember_In_VariableInitializer()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { short test = chkArtigos; } }",
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { short test = chkArtigos._Default; } }");

    [TestMethod]
    public void ExpandsDefaultMember_In_Comparison()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { if (chkArtigos == CheckBoxConstants.VbChecked) { } } }",
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { if (chkArtigos._Default == CheckBoxConstants.VbChecked) { } } }");

    [TestMethod]
    public void ExpandsDefaultMember_In_BothOperandsOf_NestedLogicalExpression()
        // Regression test: previously, expanding one side of a binary expression and then
        // falling through to base.VisitBinaryExpression re-visited the already-rewritten
        // (and, for deeper nesting, already-replaced) children, throwing because synthesized
        // nodes aren't part of the tree the SemanticModel was created for.
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); CheckBox chkArtigos2 = new CheckBox(); void M() { if (chkArtigos == CheckBoxConstants.VbChecked && chkArtigos2 == CheckBoxConstants.VbUnchecked) { } } }",
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); CheckBox chkArtigos2 = new CheckBox(); void M() { if (chkArtigos._Default == CheckBoxConstants.VbChecked && chkArtigos2._Default == CheckBoxConstants.VbUnchecked) { } } }");

    [TestMethod]
    public void ExpandsDefaultMember_Inside_NestedBinaryExpression_OnAssignmentRhs()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); CheckBox chkArtigos2 = new CheckBox(); void M() { bool result = false; result = (chkArtigos == CheckBoxConstants.VbChecked) && (chkArtigos2 == CheckBoxConstants.VbUnchecked); } }",
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); CheckBox chkArtigos2 = new CheckBox(); void M() { bool result = false; result = (chkArtigos._Default == CheckBoxConstants.VbChecked) && (chkArtigos2._Default == CheckBoxConstants.VbUnchecked); } }");

    [TestMethod]
    public void LeavesReferenceAssignmentUnchanged()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); CheckBox chkArtigos2 = new CheckBox(); void M() { chkArtigos = chkArtigos2; } }");

    [TestMethod]
    public void LeavesNullComparisonUnchanged()
        => CheckRewrites(
            CheckBoxDecl + "class T { CheckBox chkArtigos = new CheckBox(); void M() { if (chkArtigos == null) { } } }");

    [TestMethod]
    public void ExpandsDefaultMember_When_AttributeDeclaredOnImplementedInterface()
        => CheckRewrites(
            "using System.Reflection; " +
            "[DefaultMember(\"_Default\")] interface ICheckBox { } " +
            "class CheckBox : ICheckBox { public short _Default { get; set; } } " +
            "enum CheckBoxConstants : short { VbUnchecked = 0, VbChecked = 1 } " +
            "class T { CheckBox chkArtigos = new CheckBox(); void M() { chkArtigos = CheckBoxConstants.VbChecked; } }",
            "using System.Reflection; " +
            "[DefaultMember(\"_Default\")] interface ICheckBox { } " +
            "class CheckBox : ICheckBox { public short _Default { get; set; } } " +
            "enum CheckBoxConstants : short { VbUnchecked = 0, VbChecked = 1 } " +
            "class T { CheckBox chkArtigos = new CheckBox(); void M() { chkArtigos._Default = CheckBoxConstants.VbChecked; } }");

    private static void CheckRewrites(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new DefaultMemberRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
