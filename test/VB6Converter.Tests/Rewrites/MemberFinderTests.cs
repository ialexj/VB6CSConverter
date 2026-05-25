using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class MemberFinderTests
{
    [TestMethod]
    public void CorrectsWrongCasedNamedArgumentLabel()
        => CheckMember(
            "class T { void RegistaOperacao(string id, int clienteID) { } void M() { RegistaOperacao(\"Clientes\", ClienteID: 1234); } }",
            "class T { void RegistaOperacao(string id, int clienteID) { } void M() { RegistaOperacao(\"Clientes\", clienteID: 1234); } }");

    [TestMethod]
    public void LeavesCorrectlyNamedArgumentUnchanged()
        => CheckMember(
            "class T { void RegistaOperacao(string id, int clienteID) { } void M() { RegistaOperacao(\"Clientes\", clienteID: 1234); } }");

    [TestMethod]
    public void LeavesPositionalArgumentsUntouched()
        => CheckMember(
            "class T { void RegistaOperacao(string id, int clienteID) { } void M() { RegistaOperacao(\"Clientes\", 1234); } }");

    private static void CheckMember(string cs, string? expected = null)
    {
        var cu   = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter  = new MemberFinder(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected ?? cs);
    }
}
