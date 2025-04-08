using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;

namespace VB6Converter.Tests;

[TestClass]
public class CallTests
{
    [TestMethod]
    public void CallStatement() => ValidateMemberMatches(
        """
        MyFunction1 1, 2, 3, a:= 4
        Call MyFunction2(1, 2, 3, a:= 4)
        """,
        """
        MyFunction1(1, 2, 3, a: 4);
        MyFunction2(1, 2, 3, a: 4);
        """);

    [TestMethod]
    public void CallStatementWithNoParameters() => ValidateMemberMatches(
        """
        MyFunction1
        Call MyFunction2
        """,
        """
        MyFunction1();
        MyFunction2();
        """);

    [TestMethod]
    public void CallSingleMemberStatement() => ValidateMemberMatches(
        """
        obj.MyFunction 1, 2, 3, a:= 4
        Call obj.MyFunction(1, 2, 3, a:= 4)
        """,
        """
        obj.MyFunction(1, 2, 3, a: 4);
        obj.MyFunction(1, 2, 3, a: 4);
        """);

    [TestMethod]
    public void CallMultiMemberStatement() => ValidateMemberMatches(
        """
        one.two.MyFunction 1, 2, 3, a:= 4
        Call one.two.MyFunction(1, 2, 3, a:= 4)
        """,
        """
        one.two.MyFunction(1, 2, 3, a: 4);
        one.two.MyFunction(1, 2, 3, a: 4);
        """);


    [TestMethod]
    public void CallExpression() => ValidateMemberMatches(
        "x = MyFunction(1, 2, 3, a:= 4)",
        "x = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void CallSingleMemberExpression() => ValidateMemberMatches(
        "x = obj.MyFunction(1, 2, 3, a:= 4)",
        "x = obj.MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void CallMultiMemberExpression() => ValidateMemberMatches(
        "x = one.two.MyFunction(1, 2, 3, a:= 4)",
        "x = one.two.MyFunction(1, 2, 3, a: 4);"
    );


    [TestMethod]
    public void ArrayToFunctionAssignmentExpression() => ValidateMemberMatches(
        "arr(1) = MyFunction(1, 2, 3, a:= 4)",
        "arr[1] = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void ArrayMemberToFunctionAssignmentExpression() => ValidateMemberMatches(
        "one.arr(1) = MyFunction(1, 2, 3, a:= 4)",
        "one.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void ArraySubMemberToFunctionAssignmentExpression() => ValidateMemberMatches(
        "one.two.arr(1) = MyFunction(1, 2, 3, a:= 4)",
        "one.two.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );


    [TestMethod]
    public void AssignArrayPresumesFunction() => ValidateMemberMatches(
        "x = arr(1, 2)",
        "x = arr(1, 2);"
    );


    [TestMethod]
    public void AssignValue() => ValidateMemberMatches(
        "x = y",
        "x = y;"
    );

    [TestMethod]
    public void AssignValueMember() => ValidateMemberMatches(
        "x = one.y",
        "x = one.y;"
    );


    [TestMethod]
    public void AssignToDictionary() => ValidateMemberMatches(
        "x!key = v",
        "x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToMemberDictionary() => ValidateMemberMatches(
        "one.x!key = v",
        "one.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToSubMemberDictionary() => ValidateMemberMatches(
        "one.two.x!key = v",
        "one.two.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignDictionaryToValue() => ValidateMemberMatches(
        "v = x!key",
        "v = x[\"key\"];"
    );

    [TestMethod]
    public void AssignMemberDictionaryToValue() => ValidateMemberMatches(
        "v = one.x!key",
        "v = one.x[\"key\"];"
    );

    [TestMethod]
    public void AssignSubMemberDictionaryToValue() => ValidateMemberMatches(
        "v = one.two.x!key",
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
