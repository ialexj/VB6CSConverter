using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class CallTests
{
    [TestMethod]
    public void CallStatement() => ValidateBodyMatches(
        """
        MyFunction1 1, 2, 3, a:= 4
        Call MyFunction2(1, 2, 3, a:= 4)
        """,
        """
        MyFunction1(1, 2, 3, a: 4);
        MyFunction2(1, 2, 3, a: 4);
        """);

    [TestMethod]
    public void CallStatementWithNoParameters() => ValidateBodyMatches(
        """
        MyFunction1
        Call MyFunction2
        """,
        """
        MyFunction1();
        MyFunction2();
        """);

    [TestMethod]
    public void CallSingleMemberStatement() => ValidateBodyMatches(
        """
        obj.MyFunction 1, 2, 3, a:= 4
        Call obj.MyFunction(1, 2, 3, a:= 4)
        """,
        """
        obj.MyFunction(1, 2, 3, a: 4);
        obj.MyFunction(1, 2, 3, a: 4);
        """);

    [TestMethod]
    public void CallMultiMemberStatement() => ValidateBodyMatches(
        """
        one.two.MyFunction 1, 2, 3, a:= 4
        Call one.two.MyFunction(1, 2, 3, a:= 4)
        """,
        """
        one.two.MyFunction(1, 2, 3, a: 4);
        one.two.MyFunction(1, 2, 3, a: 4);
        """);


    [TestMethod]
    public void CallExpression() => ValidateBodyMatches(
        "x = MyFunction(1, 2, 3, a:= 4)",
        "x = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void CallSingleMemberExpression() => ValidateBodyMatches(
        "x = obj.MyFunction(1, 2, 3, a:= 4)",
        "x = obj.MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void CallMultiMemberExpression() => ValidateBodyMatches(
        "x = one.two.MyFunction(1, 2, 3, a:= 4)",
        "x = one.two.MyFunction(1, 2, 3, a: 4);"
    );


    [TestMethod]
    public void ArrayToFunctionAssignmentExpression() => ValidateBodyMatches(
        "arr(1) = MyFunction(1, 2, 3, a:= 4)",
        "arr[1] = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void ArrayMemberToFunctionAssignmentExpression() => ValidateBodyMatches(
        "one.arr(1) = MyFunction(1, 2, 3, a:= 4)",
        "one.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void ArraySubMemberToFunctionAssignmentExpression() => ValidateBodyMatches(
        "one.two.arr(1) = MyFunction(1, 2, 3, a:= 4)",
        "one.two.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );


    [TestMethod]
    public void AssignArrayPresumesFunction() => ValidateBodyMatches(
        "x = arr(1, 2)",
        "x = arr(1, 2);"
    );


    [TestMethod]
    public void AssignValue() => ValidateBodyMatches(
        "x = y",
        "x = y;"
    );

    [TestMethod]
    public void AssignValueMember() => ValidateBodyMatches(
        "x = one.y",
        "x = one.y;"
    );


    [TestMethod]
    public void AssignToDictionary() => ValidateBodyMatches(
        "x!key = v",
        "x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToMemberDictionary() => ValidateBodyMatches(
        "one.x!key = v",
        "one.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToSubMemberDictionary() => ValidateBodyMatches(
        "one.two.x!key = v",
        "one.two.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignDictionaryToValue() => ValidateBodyMatches(
        "v = x!key",
        "v = x[\"key\"];"
    );

    [TestMethod]
    public void AssignMemberDictionaryToValue() => ValidateBodyMatches(
        "v = one.x!key",
        "v = one.x[\"key\"];"
    );

    [TestMethod]
    public void AssignSubMemberDictionaryToValue() => ValidateBodyMatches(
        "v = one.two.x!key",
        "v = one.two.x[\"key\"];"
    );



    
}
