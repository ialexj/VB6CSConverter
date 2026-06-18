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
}
