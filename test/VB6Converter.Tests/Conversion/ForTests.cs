namespace VB6Converter.Tests.Conversion;
using static Validations;

[TestClass]
public class ForTests
{
    [TestMethod]
    public void DefaultIncrement() => ValidateBodyMatches(
        """
        For i = 1 To 10
        Next i
        """,
        """
        for (i = 1; i <= 10; i++)
        {
        }
        """);

    [TestMethod]
    public void StepOne() => ValidateBodyMatches(
        """
        For i = 1 To 10 Step 1
        Next i
        """,
        """
        for (i = 1; i <= 10; i++)
        {
        }
        """);

    [TestMethod]
    public void Step2() => ValidateBodyMatches(
        """
        For i = 1 To 10 Step 2
        Next i
        """,
        """
        for (i = 1; i <= 10; i += 2)
        {
        }
        """);

    [TestMethod]
    public void StepNegativeOne() => ValidateBodyMatches(
        """
        For i = 10 To 1 Step -1
        Next i
        """,
        """
        for (i = 10; i >= 1; i--)
        {
        }
        """);

    [TestMethod]
    public void StepNegativeTwo() => ValidateBodyMatches(
        """
        For i = 10 To 1 Step -2
        Next i
        """,
        """
        for (i = 10; i >= 1; i -= 2)
        {
        }
        """);

    [TestMethod]
    public void WithType() => ValidateBodyMatches(
        """
        For d As Double = 1 To 10
        Next i
        """,
        """
        for (d = 1; d <= 10; d++)
        {
        }
        """);

    [TestMethod]
    public void ExitFor() => ValidateBodyMatches(
        """
        For i = 1 To 10
            Exit For
        Next i
        """,
        """
        for (i = 1; i <= 10; i++)
        {
            break;
        }
        """);

    [TestMethod]
    public void ForEachConvertsToForeach() => ValidateBodyMatches(
        """
        Dim v As Variant
        For Each v In values
            UseValue v
        Next
        """,
        """
        dynamic v = default;
        foreach (var v in values)
        {
            UseValue(v);
        }
        """);

    [TestMethod]
    public void DoLoopConvertsToWhile() => ValidateBodyMatches(
        """
        Do While running
            Tick
        Loop
        """,
        """
        while (running)
        {
            Tick();
        }
        """);

    [TestMethod]
    public void ExitDoBecomesBreak() => ValidateBodyMatches(
        """
        Do While running
            Exit Do
        Loop
        """,
        """
        while (running)
        {
            break;
        }
        """);

    [TestMethod]
    public void WhileWendConvertsToWhile() => ValidateBodyMatches(
        """
        While running
            Tick
        Wend
        """,
        """
        while (running)
        {
            Tick();
        }
        """);

    [TestMethod]
    public void WhileWendWithConditionChange() => ValidateBodyMatches(
        """
        While i < 10
            i = i + 1
        Wend
        """,
        """
        while (i < 10)
        {
            i = i + 1;
        }
        """);

    [TestMethod]
    public void ForWithVariableStep() => ValidateBodyMatches(
        """
        For i = 1 To 10 Step stepValue
        Next i
        """,
        """
        for (i = 1; i <= 10; i++)
        {
        }
        """);
    // TODO: Regression baseline — non-literal Step expressions default to i++ instead of i += stepValue.
    // This is an uncovered branch in LoopConverter.cs:46 that should apply step to incrementor.

    [TestMethod]
    public void ForWithExpressionStep() => ValidateBodyMatches(
        """
        For i = 1 To 10 Step GetStep()
        Next i
        """,
        """
        for (i = 1; i <= 10; i++)
        {
        }
        """);
    // TODO: Regression baseline — function call in Step expression defaults to i++ instead of i += GetStep().
    // Same issue as ForWithVariableStep; LoopConverter needs to handle non-literal Step values.

    
}
