using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters;


namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class DisambiguatorTests
{
    [TestMethod]
    public void DisambiguatesLocal()
    {
        var cs = """
            int[] v = new int[5];
            int x = v(0);

            void Test() { }
            Test;
            """;

        var expected = """
            int[] v = new int[5];
            int x = v[0];

            void Test() { }
            Test();
            """;

        CheckDisambiguation(cs, expected);
    }

    [TestMethod]
    public void DisambiguatesClass()
    {
        var cs = """
            class TestClass 
                int[] v;
                void A() { }
                void Test() {
                    int x = v(0);
                    A;
                }
            }
            """;

        var expected = """
            class TestClass 
                int[] v;
                void A() { }
                void Test() {
                    int x = v[0];
                    A();
                }
            }
            """;

        CheckDisambiguation(cs, expected);
    }

    [TestMethod]
    public void DisambiguatesQualified()
    {
        var cs = """
            using static A;
            public static class A {
                public static int[] v;
                public static void Method() { }
            }
            class TestClass
                void Test() {
                    int x = v(0);
                    Method;
                }
            }
            """;

        var expected = """
            using static A;
            public static class A {
                public static int[] v;
                public static void Method() { }
            }
            class TestClass
                void Test() {
                    int x = v[0];
                    Method();
                }
            }
            """;

        CheckDisambiguation(cs, expected);
    }

    [TestMethod]
    public void DisambiguatesMemberAccess()
    {
        var cs = """
            public static class A {
                public static int[] v;
                public static void Method() { }
            }
            class TestClass
                void Test() {
                    int x = A.v(0);
                    A.Method();
                    A.Method;
                }
            }
            """;

        var expected = """
            public static class A {
                public static int[] v;
                public static void Method() { }
            }
            class TestClass
                void Test() {
                    int x = A.v[0];
                    A.Method();
                    A.Method();
                }
            }
            """;

        CheckDisambiguation(cs, expected);
    }

    [TestMethod]
    public void Test()
    {
        var cs = """
            class Test
            {
                object v;
                void Test() {
                    int x = v(0);
                }
            }
            """;

        var expected = """
            class Test
            {
                object[] v;
                void Test() {
                    int x = v[0];
                }
            }
            """;

        CheckDisambiguation(cs, expected);
    }

    private static void CheckDisambiguation(string cs, string expected)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new ArrayCallDisambiguator(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected);
    }
}
