using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class LabelCollapsingTests
{
    [TestMethod]
    public void GoToEmptyLabel_Collapses() => ValidateBodyMatches(
        """
        GoTo End
        x = 1
        End:
        Exit Sub
        """,
        """
        return;
        x = 1;
        End:
            return;
        """);

    [TestMethod]
    public void GoToLabelBeforeReturn_Collapses() => ValidateBodyMatches(
        """
        GoTo End
        x = 1
        End:
        Exit Sub
        """,
        """
        return;
        x = 1;
        End:
            return;
        """);

    [TestMethod]
    public void GoToLabelBeforeReturnValue_Collapses() => ValidateBodyMatches(
        """
        GoTo End
        x = 1
        End:
        Exit Function
        """,
        """
        return;
        x = 1;
        End:
            return;
        """);

    [TestMethod]
    public void GoToLabelWithStatementsAfter_NotCollapsed() => ValidateBodyMatches(
        """
        GoTo Middle
        x = 1
        Middle:
        y = 2
        z = 3
        """,
        """
        goto Middle;
        x = 1;
        Middle:
            y = 2;
        z = 3;
        """);

    [TestMethod]
    public void GoToInsideIfBlock_StillCollapses() => ValidateBodyMatches(
        """
        If True Then
            GoTo End
        End If
        x = 1
        End:
        Exit Sub
        """,
        """
        if (true)
        {
            return;
        }
        x = 1;
        End:
            return;
        """);

    [TestMethod]
    public void MultipleGoTosToSameLabel_AllCollapse() => ValidateBodyMatches(
        """
        If x = 1 Then
            GoTo End
        Else
            GoTo End
        End If
        x = 1
        End:
        Exit Sub
        """,
        """
        if (x == 1)
        {
            return;
        }
        else
        {
            return;
        }
        x = 1;
        End:
            return;
        """);

    [TestMethod]
    public void GoToNonTerminalLabel_NotCollapsed() => ValidateBodyMatches(
        """
        GoTo Start
        Start:
        x = 1
        y = 2
        """,
        """
        goto Start;
        Start:
            x = 1;
        y = 2;
        """);

    [TestMethod]
    public void GoToEmptyLabelWithComments_Collapses() => ValidateBodyMatches(
        """
        GoTo End
        ' some comment
        End:
        Exit Sub
        """,
        """
        return;
        End:
            return;
        """);

    [TestMethod]
    public void NoGoTo_NoCollapsing() => ValidateBodyMatches(
        """
        x = 1
        Label1:
        y = 2
        """,
        """
        x = 1;
        Label1:
            y = 2;
        """);
}

