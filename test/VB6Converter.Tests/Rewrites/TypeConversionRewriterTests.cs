using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class TypeConversionRewriterTests
{
    [TestMethod]
    public void ConvertsIntToStringInAssignment()
        => CheckRewrites(
            "class T { void M() { int i = 1; string a = null; a = i; } }",
            "class T { void M() { int i = 1; string a = null; a = System.Convert.ToString(i); } }");

    [TestMethod]
    public void ConvertsIntToStringInInitializer()
        => CheckRewrites(
            "class T { void M() { int i = 1; string a = i; } }",
            "class T { void M() { int i = 1; string a = System.Convert.ToString(i); } }");

    [TestMethod]
    public void ConvertsIntToStringArgument()
        => CheckRewrites(
            "class T { void Take(string s) { } void M() { int i = 1; Take(i); } }",
            "class T { void Take(string s) { } void M() { int i = 1; Take(System.Convert.ToString(i)); } }");

    [TestMethod]
    public void ConvertsIntToStringReturn()
        => CheckRewrites(
            "class T { string M() { int i = 1; return i; } }",
            "class T { string M() { int i = 1; return System.Convert.ToString(i); } }");

    [TestMethod]
    public void ConvertsDoubleToIntAssignment()
        => CheckRewrites(
            "class T { void M() { double d = 8.9; int i = d; } }",
            "class T { void M() { double d = 8.9; int i = System.Convert.ToInt32(d); } }");

    [TestMethod]
    public void ConvertsStringToIntAssignment()
        => CheckRewrites(
            "class T { void M() { string s = \"12\"; int i = s; } }",
            "class T { void M() { string s = \"12\"; int i = System.Convert.ToInt32(s); } }");

    [TestMethod]
    public void ConvertsBoolFalseToIntAssignment()
        => CheckRewrites(
            "class T { void M() { long l = 0L; l = false; } }",
            "class T { void M() { long l = 0L; l = 0; } }");

    [TestMethod]
    public void ConvertsBoolTrueToIntAssignment()
        => CheckRewrites(
            "class T { void M() { long l = 0L; l = true; } }",
            "class T { void M() { long l = 0L; l = -1; } }");

    [TestMethod]
    public void ConvertsBoolFalseToIntInitializer()
        => CheckRewrites(
            "class T { void M() { long l = false; } }",
            "class T { void M() { long l = 0; } }");

    [TestMethod]
    public void ConvertsBoolTrueToIntInitializer()
        => CheckRewrites(
            "class T { void M() { long l = true; } }",
            "class T { void M() { long l = -1; } }");

    [TestMethod]
    public void ConvertsBoolExpressionToIntAssignment()
        => CheckRewrites(
            "class T { void M() { bool b = true; long l = 0L; l = b; } }",
            "class T { void M() { bool b = true; long l = 0L; l = -(System.Convert.ToInt64(b)); } }");

    [TestMethod]
    public void BoolToIntIsIdempotent()
    {
        const string source = "class T { void M() { bool b = true; long l = 0L; l = b; } }";
        var firstPass = RewriteWithFreshSemantics(source);
        var secondPass = RewriteWithFreshSemantics(firstPass);
        secondPass.Should().Be(firstPass);
    }

    [TestMethod]
    public void LeavesImplicitNumericConversionUnchanged()
        => CheckRewrites(
            "class T { void M() { int i = 1; long l = i; } }");

    [TestMethod]
    public void LeavesExistingCastUnchanged()
        => CheckRewrites(
            "class T { void M() { double d = 8.9; int i = (int)d; } }");

    [TestMethod]
    public void LeavesExistingConvertUnchanged()
        => CheckRewrites(
            "class T { void M() { int i = 1; string s = System.Convert.ToString(i); } }");

    [TestMethod]
    public void LeavesEnumConversionForEnumRewriter()
        => CheckRewrites(
            "enum E : short { A = 1 } class T { void M() { short x = E.A; } }");

    [TestMethod]
    public void LeavesArgumentUnchangedWhenOverloadsDisagreeOnTargetType()
        => CheckRewrites(
            "class T { void Log(string s) { } void Log(bool b) { } void M() { Log(\"Versao: \" + 1); } }");

    [TestMethod]
    public void LeavesToBooleanCallUnchanged()
        => CheckRewrites(
            "class T { string M() { return System.Convert.ToString(System.Convert.ToBoolean(default)); } }");

    [TestMethod]
    public void ConvertsStringToIntInEqualityComparison()
        => CheckRewrites(
            "class T { void M() { int i = 1; string s = \"1\"; bool b = i == s; } }",
            "class T { void M() { int i = 1; string s = \"1\"; bool b = i == System.Convert.ToInt32(s); } }");

    [TestMethod]
    public void ConvertsStringToDoubleInLessThanComparison()
        => CheckRewrites(
            "class T { void M() { double d = 1.0; string s = \"1\"; bool b = s < d; } }",
            "class T { void M() { double d = 1.0; string s = \"1\"; bool b = System.Convert.ToDouble(s) < d; } }");

    [TestMethod]
    public void ConvertsStringToIntInNotEqualsComparison()
        => CheckRewrites(
            "class T { void M() { int i = 1; string s = \"1\"; bool b = i != s; } }",
            "class T { void M() { int i = 1; string s = \"1\"; bool b = i != System.Convert.ToInt32(s); } }");

    [TestMethod]
    public void LeavesStringToStringComparisonUnchanged()
        => CheckRewrites(
            "class T { void M() { string s1 = \"a\"; string s2 = \"b\"; bool b = s1 == s2; } }");

    [TestMethod]
    public void LeavesIntToIntComparisonUnchanged()
        => CheckRewrites(
            "class T { void M() { int i = 1; int j = 2; bool b = i == j; } }");

    [TestMethod]
    public void LeavesNonComparisonBinaryExpressionUnchanged()
        => CheckRewrites(
            "class T { void M() { int i = 1; string s = \"1\"; var x = i + s; } }");

    [TestMethod]
    public void LeavesExistingConvertInComparisonUnchanged()
        => CheckRewrites(
            "class T { void M() { int i = 1; string s = \"1\"; bool b = i == System.Convert.ToInt32(s); } }");

    [TestMethod]
    public void StringComparisonIsIdempotentAcrossPasses()
    {
        const string source = "class T { void M() { int i = 1; string s = \"1\"; bool b = i == s; } }";

        var firstPass = RewriteWithFreshSemantics(source);
        var secondPass = RewriteWithFreshSemantics(firstPass);
        var thirdPass = RewriteWithFreshSemantics(secondPass);

        secondPass.Should().Be(firstPass);
        thirdPass.Should().Be(firstPass);
    }

    [TestMethod]
    public void DoesNotChainToBooleanAcrossPasses()
    {
        const string source = "class T { string M() { return System.Convert.ToString(System.Convert.ToBoolean(System.Convert.ToBoolean(System.Convert.ToBoolean(default)))); } }";

        var firstPass = RewriteWithFreshSemantics(source);
        var secondPass = RewriteWithFreshSemantics(firstPass);
        var thirdPass = RewriteWithFreshSemantics(secondPass);

        secondPass.Should().Be(firstPass);
        thirdPass.Should().Be(firstPass);
    }

    private static void CheckRewrites(string cs, string? expected = null)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new TypeConversionRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }

    private static string RewriteWithFreshSemantics(string cs)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new TypeConversionRewriter(semantics);

        var rewritten = (CompilationUnitSyntax)rewriter.Visit(cu);
        return rewritten.ToFullString();
    }
}
