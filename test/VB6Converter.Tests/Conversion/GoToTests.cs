using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class GoToTests
{
    [TestMethod]
    public void LineLabelGoto() => ValidateBodyMatches(
        """
        GoTo Label1
        Label1:
        Exit Sub
        """,
        """
        return;
        Label1:
            return;
        """);

    [TestMethod]
    public void ResumeWithLabelBecomesGoto() => ValidateBodyMatches(
        """
        Resume handler
        """,
        """
        goto handler;
        """);

    [TestMethod]
    public void OnErrorGotoWithLabelBecomesTryCatch() => ValidateBodyMatches(
        """
        On Error GoTo handler
        x = 1
        handler:
        y = 2
        """,
        """
        try
        {
            x = 1;
        }
        catch
        {
            y = 2;
        }
        """);

    [TestMethod]
    public void OnErrorGotoWithExitLabelAndResumeBecomesStructuredTryCatch() => ValidateBodyMatches(
        """
        On Error GoTo handler_Err
        x = 1
        GoTo handler_End
        x = 2
        handler_End:
        Exit Sub
        handler_Err:
        x = 0
        Resume handler_End
        """,
        """
        try
        {
            x = 1;
            goto handler_End;
            x = 2;
        }
        catch
        {
            x = 0;
            goto handler_End;
        }
        handler_End:
            return;
        """);

    [TestMethod]
    public void OnErrorGotoErrorLabelNotFirstLabelInBlock() => ValidateBodyMatches(
        """
        On Error GoTo handler_Err
        x = 1
        handler_End:
        Exit Sub
        handler_Err:
        x = 0
        Resume handler_End
        """,
        """
        try
        {
            x = 1;
        }
        catch
        {
            x = 0;
            goto handler_End;
        }
        handler_End:
            return;
        """);

    [TestMethod]
    public void OnErrorGotoWithGotoToRootLabelUsesCatchGotoFallback() => ValidateBodyMatches(
        """
        On Error GoTo handler
        x = 1
        GoTo done
        handler:
        x = 0
        done:
        x = 2
        Exit Sub
        """,
        """
        try
        {
            x = 1;
            goto done;
        }
        catch
        {
            goto handler;
        }
        handler:
            x = 0;
        done:
            x = 2;
        return;
        """);

    [TestMethod]
    public void MultipleOnErrorClausesAreRewritten() => ValidateBodyMatches(
        """
        On Error GoTo first_Err
        a = 1
        GoTo first_End
        first_Err:
        a = 0
        first_End:
        b = 10

        On Error GoTo second_Err
        b = 1
        second_Err:
        b = 0
        """,
        """
        try
        {
            a = 1;
            goto first_End;
        }
        catch
        {
            goto first_Err;
        }
        first_Err:
            a = 0;
        first_End:
            b = 10;
        try
        {
            b = 1;
        }
        catch
        {
            b = 0;
        }
        """);
}
