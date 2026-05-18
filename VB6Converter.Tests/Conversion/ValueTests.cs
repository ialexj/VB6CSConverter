using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class ValueTests
{
    [TestMethod]
    public void TrueLiteral() => ValidateBodyMatches(
        """
        x = True
        """,
        """
        x = true;
        """);

    [TestMethod]
    public void FalseLiteral() => ValidateBodyMatches(
        """
        x = False
        """,
        """
        x = false;
        """);

    [TestMethod]
    public void NothingLiteral() => ValidateBodyMatches(
        """
        x = Nothing
        """,
        """
        x = null;
        """);

    [TestMethod]
    public void NullLiteral() => ValidateBodyMatches(
        """
        x = Null
        """,
        """
        x = null;
        """);

    [TestMethod]
    public void IntegerLiteral() => ValidateBodyMatches(
        """
        x = 42
        """,
        """
        x = 42;
        """);

    [TestMethod]
    public void DoubleLiteral() => ValidateBodyMatches(
        """
        x = 1.5
        """,
        """
        x = 1.5;
        """);

    [TestMethod]
    public void StringLiteral() => ValidateBodyMatches(
        """
        x = "abc"
        """,
        """
        x = "abc";
        """);

    [TestMethod]
    public void DateLiteralUsesDateTimeParse() => ValidateBodyMatches(
        """
        x = #2020-01-02#
        """, 
        """
        x = DateTime.Parse("2020-01-02");
        """);

    [TestMethod]
    public void NotEqualsInversion() => ValidateBodyMatches(
        """
        x = Not (a = b)
        """,
        """
        x = !(a == b);
        """);

    [TestMethod]
    public void NotNotEqualsInversion() => ValidateBodyMatches(
        """
        x = Not (a <> b)
        """,
        """
        x = !(a != b);
        """);

    [TestMethod]
    public void NotIsNullInversion() => ValidateBodyMatches(
        """
        x = Not (a Is Null)
        """,
        """
        x = !(a is null);
        """);

    [TestMethod]
    public void PowerOperatorUsesMathPow() => ValidateBodyMatches(
        """
        x = a ^ b
        """,
        """
        x = Math.Pow(a, b);
        """);

    [TestMethod]
    public void AmpersandIsStringConcat() => ValidateBodyMatches(
        """
        x = a & b
        """,
        """
        x = a + b;
        """);

    [TestMethod]
    public void ModOperator() => ValidateBodyMatches(
        """
        x = a Mod b
        """,
        """
        x = a % b;
        """);

    [TestMethod]
    public void XorOperator() => ValidateBodyMatches(
        """
        x = a Xor b
        """,
        """
        x = a ^ b;
        """);

    [TestMethod]
    public void IsOperator() => ValidateBodyMatches(
        """
        x = a Is b
        """,
        """
        x = a is b;
        """);
}
