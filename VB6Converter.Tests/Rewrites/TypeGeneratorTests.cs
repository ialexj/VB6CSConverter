using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class TypeGeneratorTests
{
    [TestMethod]
    public void SimpleName()
    {
        var myclass = ExpectMyClass("""
            public class TestClass
            {
                public MyClass something;
                
                public void Test()
                {
                    something = new MyClass();
                    something.AMethod(1, "test");
                    something.Property = "abc";
                    something.Property2 = 123;
                }
            }
            """);

        myclass.Parent.Should().BeOfType<CompilationUnitSyntax>();
    }

    [TestMethod]
    public void Qualified()
    {
        var myclass = ExpectMyClass("""
            public class TestClass
            {
                public VB.MyClass something;
                
                public void Test()
                {
                    something = new VB.MyClass();
                    something.AMethod(1, "test");
                    something.Property = "abc";
                    something.Property2 = 123;
                }
            }
            """);

        var ns = myclass.Parent.Should().BeAssignableTo<BaseNamespaceDeclarationSyntax>().Which;
        ns.Name.ToString().Should().Be("VB");
    }

    [TestMethod]
    public void This()
    {
        var generated = ExpectMyClass("""
            public class TestClass : VB.MyClass
            {                
                public void Test()
                {
                    this.AMethod(1, "test");
                    this.Property = "abc";
                    this.Property2 = 123;
                }
            }
            """);
    }

    [TestMethod]
    public void SomethingElse()
    {
        var generated = TestGenerator("""
            public class TestClass
            {
                string someKnownIdentifier = "test";

                public void Test()
                {
                    var y = someKnownIdentifier;
                    var x = someUnknownIdentifier;
                }
            }
            """);

        var cls = generated.Should().ContainSingle().Which;
        cls.Identifier.Text.Should().Be("_MissingMembers");
        var field = cls.Members[0].Should().BeOfType<FieldDeclarationSyntax>().Which;
        var variable = field.Declaration.Variables.Should().ContainSingle().Which;
        variable.Identifier.Text.Should().Be("someUnknownIdentifier");
    }


    ClassDeclarationSyntax ExpectMyClass(string cs)
    {
        var classes = TestGenerator(cs);
        var myclass = classes.Should().ContainSingle().Which;
        myclass.Identifier.Value.Should().Be("MyClass");

        myclass.Members.Should().HaveCount(3);
        var method = myclass.Members.OfType<MethodDeclarationSyntax>().Should().ContainSingle(c => c.Identifier.Text == "AMethod").Which;
        method.ParameterList.Parameters.Should().HaveCount(2);

        var prop1 = myclass.Members.OfType<PropertyDeclarationSyntax>().Should().ContainSingle(c => c.Identifier.Text == "Property").Which;
        var prop2 = myclass.Members.OfType<PropertyDeclarationSyntax>().Should().ContainSingle(c => c.Identifier.Text == "Property2").Which;

        return myclass;
    }


    ClassDeclarationSyntax[] TestGenerator(string cs)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new ArrayCallDisambiguator(semantics);

        var types = new MissingTypes();

        var generator = new MissingTypeScanner(semantics, types);
        generator.Visit(cu);

        return types.GetCompilationUnits()
            .SelectMany(cu => cu.DescendantNodes(n => n is not ClassDeclarationSyntax).OfType<ClassDeclarationSyntax>())
            .ToArray();
    }
}
