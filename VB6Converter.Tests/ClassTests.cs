using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;

namespace VB6Converter.Tests;

[TestClass]
public sealed class ClassTests
{

    [TestMethod]
    public void MethodArguments()
    {
        var cu = VB6ToCSharpConverter.GetCompilationUnit(
            """
            Public Sub Test(ByVal arg1 As String, ByRef arg2 As Long, ByVal another As Variant)
            End Sub
            """,
            nameof(MethodArguments));

        cu.Members
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .Should().ContainSingle()
            .Which.Members.OfType<ClassDeclarationSyntax>()
                .Should().ContainSingle()
                .Which.ToFullString().Should().Be(
                    """
                    public partial static class MethodArguments
                    {
                        public static void Test(string arg1, int arg2, object another)
                        {
                        }
                    }
                    """);
    }

    [TestMethod]
    public void FunctionReturn()
    {
        var cu = VB6ToCSharpConverter.GetCompilationUnit(
            """
            Public Function Test() As Boolean
            	If SomeVariable Then
            		Test = True
            	End If
            	DoSomethingElse
            	If SomeOtherVariable Then
            		Exit Function
            	End If
            End Function
            """,
            nameof(FunctionReturn));

        cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>()
            .Should().ContainSingle().Which
            .Members.OfType<ClassDeclarationSyntax>()
                .Should().ContainSingle()
                .Which.ToFullString().Should().Be(
                    """
                    public partial static class FunctionReturn
                    {
                        public static bool Test()
                        {
                            bool Test = default;
                            if (SomeVariable)
                                Test = true;
                            DoSomethingElse();
                            if (SomeOtherVariable)
                                return Test;
                            return Test;
                        }
                    }
                    """);
    }

    [TestMethod]
    public void PropertyGetSet()
    {
        ValidateClassMatches(
            """
            Public Property Get Test() As String
                Test = testVar
            End Property
            
            Public Property Let Test(ByVal someValue As String)
                testVar = someValue
            End Property
            """,
            """
            public partial static class PropertyGetSet
            {
                public static string Test
                {
                    get
                    {
                        string Test = default;
                        Test = testVar;
                        return Test;
                    }

                    set
                    {
                        testVar = value;
                    }
                }
            }
            """);

    }


    static void ValidateClassMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = VB6ToCSharpConverter.GetCompilationUnit(vb, name);
        var expected = VB6ToCSharpConverter.ConvertString(cs, name);

        cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>()
            .Should().ContainSingle().Which
            .Members.OfType<ClassDeclarationSyntax>()
                .Should().ContainSingle()
                .Which.ToFullString().Should().Be(cs);
    }
}
