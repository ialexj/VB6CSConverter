using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class WithCallTests
{
    [TestMethod]
    public void CallSingleMemberStatement() => ValidateBodyMatches(
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
    public void CallMultiMemberStatement() => ValidateBodyMatches(
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
    public void CallSingleMemberExpression() => ValidateBodyMatches(
        """
        With obj
            x = .MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "x = obj.MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void CallMultiMemberExpression() => ValidateBodyMatches(
        """
        With one.two
            x = .MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "x = one.two.MyFunction(1, 2, 3, a: 4);"
    );


    [TestMethod]
    public void ArrayMemberToFunctionAssignmentExpression() => ValidateBodyMatches(
        """
        With one
            .arr(1) = MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "one.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );

    [TestMethod]
    public void ArraySubMemberToFunctionAssignmentExpression() => ValidateBodyMatches(
        """
        With one.two
            .arr(1) = MyFunction(1, 2, 3, a:= 4)
        End With
        """,
        "one.two.arr[1] = MyFunction(1, 2, 3, a: 4);"
    );



    [TestMethod]
    public void AssignValueMember() => ValidateBodyMatches(
        """
        With one
            x = .y
        End With
        """,
        "x = one.y;"
    );


    [TestMethod]
    public void AssignToDictionary() => ValidateBodyMatches(
        """
        With x
            !key = v
        End With
        """,
        "x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToMemberDictionary() => ValidateBodyMatches(
        """
        With one.x
            !key = v
        End With
        """,
        "one.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignToSubMemberDictionary() => ValidateBodyMatches(
        """
        With one.two.x
            !key = v
        End With
        """,
        "one.two.x[\"key\"] = v;"
    );

    [TestMethod]
    public void AssignDictionaryToValue() => ValidateBodyMatches(
        """
        With x
            v = !key
        End With
        """,
        "v = x[\"key\"];"
    );

    [TestMethod]
    public void AssignMemberDictionaryToValue() => ValidateBodyMatches(
        """
        With one.x
            v = !key
        End With
        """,
        "v = one.x[\"key\"];"
    );

    [TestMethod]
    public void AssignSubMemberDictionaryToValue() => ValidateBodyMatches(
        """
        With one.two.x
            v = !key
        End With
        """,
        "v = one.two.x[\"key\"];"
    );

    [TestMethod]
    public void WithInExpression() => ValidateBodyMatches(
        """
        With rsTmp
            If (.RecordCount > 0) Then
            End If
        End With
        """,
        """
        if ((rsTmp.RecordCount > 0))
        {
        }
        """
        );
}
