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
        goto Label1;
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
}
