using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;

namespace VB6Converter.Tests;

[TestClass]
public class WithCallTests
{
    [TestMethod]
    public void CallSingleMemberStatement() => ValidateMemberMatches(
        """
        With obj
            .MyFunction 1, 2, 3, a:= 4
            Call .MyFunction(1, 2, 3, a:= 4)
        End With 
        """,
        """
        {
            obj.MyFunction(1, 2, 3, a: 4);
            obj.MyFunction(1, 2, 3, a: 4);
        }
        """);

    [TestMethod]
    public void CallMultiMemberStatement() => ValidateMemberMatches(
        """
        With one.two
            .MyFunction 1, 2, 3, a:= 4
            Call .MyFunction(1, 2, 3, a:= 4)
        End With 
        """,
        """
        {
            one.two.MyFunction(1, 2, 3, a: 4);
            one.two.MyFunction(1, 2, 3, a: 4);
        }
        """);



    [TestMethod]
    public void CallSingleMemberExpression() => ValidateMemberMatches(
        """
        With obj
            x = .MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "x = obj.MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void CallMultiMemberExpression() => ValidateMemberMatches(
        """
        With one.two
            x = .MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "x = one.two.MyFunction(1, 2, 3, a: 4);"
    );


    [TestMethod]
    public void ArrayMemberToFunctionAssignmentExpression() => ValidateMemberMatches(
        """
        With one
            .arr(1) = MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "one.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void ArraySubMemberToFunctionAssignmentExpression() => ValidateMemberMatches(
        """
        With one.two
            .arr(1) = MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "one.two.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );



    [TestMethod]
    public void AssignValueMember() => ValidateMemberMatches(
        """
        With one
            x = .y
        End With
        """,
        "x = one.y;"
    );


    [TestMethod]
    public void AssignToDictionary() => ValidateMemberMatches(
        """
        With x
            !key = v
        End With
        """,
        "x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToMemberDictionary() => ValidateMemberMatches(
        """
        With one.x
            !key = v
        End With
        """,
        "one.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToSubMemberDictionary() => ValidateMemberMatches(
        """
        With one.two.x
            !key = v
        End With
        """,
        "one.two.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignDictionaryToValue() => ValidateMemberMatches(
        """
        With x
            v = !key
        End With
        """,
        "v = x[\"key\"];"
    );

    [TestMethod]
    public void AssignMemberDictionaryToValue() => ValidateMemberMatches(
        """
        With one.x
            v = !key
        End With
        """,
        "v = one.x[\"key\"];"
    );

    [TestMethod]
    public void AssignSubMemberDictionaryToValue() => ValidateMemberMatches(
        """
        With one.two.x
            v = !key
        End With
        """,
        "v = one.two.x[\"key\"];"
    );

    static void ValidateMemberMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var wrapper = $"""
            Sub Test()
                {vb}
            End Sub
            """;

        var cu = VB6ToCSharpConverter.GetCompilationUnit(wrapper, name);
        var ns = (FileScopedNamespaceDeclarationSyntax)cu.Members[0];
        var cls = (ClassDeclarationSyntax)ns.Members[0];
        var met = (MethodDeclarationSyntax)cls.Members[0];

        var sb = new StringBuilder();
        foreach (var m in met.Body!.Statements) {
            sb.AppendLine(m.NormalizeWhitespace().ToFullString());
        }

        string converted = sb.ToString().TrimEnd(Environment.NewLine.ToCharArray());
        converted.Should().Be(cs);
    }
}
