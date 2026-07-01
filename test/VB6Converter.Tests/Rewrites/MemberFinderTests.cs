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

    [TestMethod]
    public void CorrectsGetterInvocationForGetSetPair()
        // obj.item(k) → obj.GetItem(k) when the type exposes GetItem(int) but no "item" member.
        => CheckMember(
            "class XArr { public object GetItem(int k) => null; } class T { XArr obj = new(); void M() { var x = obj.item(0); } }",
            "class XArr { public object GetItem(int k) => null; } class T { XArr obj = new(); void M() { var x = obj.GetItem(0); } }");

    [TestMethod]
    public void CorrectsSetterElementAccessForGetSetPair()
        // obj.item[k] = v → obj.Item[k] = v (canonical name without "Get" prefix) so that
        // ParameterizedPropertyRewriter can resolve SetItem via "Set" + "Item".
        => CheckMember(
            "class XArr { public object GetItem(int k) => null; public void SetItem(int k, object v) { } } class T { XArr obj = new(); void M() { obj.item[0] = null; } }",
            "class XArr { public object GetItem(int k) => null; public void SetItem(int k, object v) { } } class T { XArr obj = new(); void M() { obj.Item[0] = null; } }");

    private static void CheckMember(string cs, string? expected = null)
    {
        var cu   = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter  = new SymbolCapitalizationRewriter(semantics);

        var newCu = rewriter.Visit(cu);
        var actual = CSharpSyntaxTree.ParseText(newCu!.ToFullString()).GetRoot().NormalizeWhitespace().ToFullString();
        var expectedText = CSharpSyntaxTree.ParseText(expected ?? cs).GetRoot().NormalizeWhitespace().ToFullString();
        actual.Should().Be(expectedText);
    }
}
