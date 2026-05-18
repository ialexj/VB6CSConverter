using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class SwitchTests
{
    [TestMethod]
    public void SimpleSwitch() => ValidateBodyMatches(
        """
        Select Case idx
            Case 0
                Something
            Case Else
                SomethingElse
        End Select
        """,
        """
        switch (idx)
        {
            case 0:
                Something();
                break;
            default:
                SomethingElse();
                break;
        }
        """);

    [TestMethod]
    public void ToSwitch() => ValidateBodyMatches(
        """
        Select Case idx
            Case 0 To 10
                Something
            Case Else
                SomethingElse
        End Select
        """,
        """
        if (idx >= 0 && idx <= 10)
        {
            Something();
        }
        else
        {
            SomethingElse();
        }
        """);

    [TestMethod]
    public void IsSwitch() => ValidateBodyMatches(
        """
        Select Case idx
            Case Is >= 10
                Something1
            Case Is > 10
                Something2
            Case Is <= 10
                Something3
            Case Is < 10
                Something4
            Case Is = 10
                Something5
            Case Is <> 10
                Something6
            Case Else
                SomethingElse
        End Select
        """,
        """
        if (idx >= 10)
        {
            Something1();
        }
        else if (idx > 10)
        {
            Something2();
        }
        else if (idx <= 10)
        {
            Something3();
        }
        else if (idx < 10)
        {
            Something4();
        }
        else if (idx == 10)
        {
            Something5();
        }
        else if (idx != 10)
        {
            Something6();
        }
        else
        {
            SomethingElse();
        }
        """);

    [TestMethod]
    public void MultipleValuesSwitch() => ValidateBodyMatches(
        """
        Select Case idx
            Case 1, 2, 3
                A
            Case Else
                B
        End Select
        """,
        """
        switch (idx)
        {
            case 1:
            case 2:
            case 3:
                A();
                break;
            default:
                B();
                break;
        }
        """);

    [TestMethod]
    public void ToRangeFallbackIfChain() => ValidateBodyMatches(
        """
        Select Case idx
            Case 1 To 5
                A
            Case Else
                B
        End Select
        """,
        """
        if (idx >= 1 && idx <= 5)
        {
            A();
        }
        else
        {
            B();
        }
        """);
}
