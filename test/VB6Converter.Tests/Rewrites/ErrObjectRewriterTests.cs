using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class ErrObjectRewriterTests
{
    [TestMethod]
    public void ErrNumber_BecomesInformationErrCall() => ValidateBodyMatches(
        """
        x = Err.Number
        """,
        """
        x = Microsoft.VisualBasic.Information.Err().Number;
        """);

    [TestMethod]
    public void ErrDescription_BecomesInformationErrCall() => ValidateBodyMatches(
        """
        x = Err.Description
        """,
        """
        x = Microsoft.VisualBasic.Information.Err().Description;
        """);

    [TestMethod]
    public void ErrSourceHelpFileHelpContext_BecomeInformationErrCalls() => ValidateBodyMatches(
        """
        a = Err.Source
        b = Err.HelpFile
        c = Err.HelpContext
        """,
        """
        a = Microsoft.VisualBasic.Information.Err().Source;
        b = Microsoft.VisualBasic.Information.Err().HelpFile;
        c = Microsoft.VisualBasic.Information.Err().HelpContext;
        """);

    [TestMethod]
    public void ErrClear_BecomesInformationErrCall() => ValidateBodyMatches(
        """
        Err.Clear
        """,
        """
        Microsoft.VisualBasic.Information.Err().Clear();
        """);

    [TestMethod]
    public void BareErl_BecomesInformationErlCall() => ValidateBodyMatches(
        """
        x = Erl
        """,
        """
        x = Microsoft.VisualBasic.Information.Erl();
        """);

    [TestMethod]
    public void ErlInConcatenation_BecomesInformationErlCall() => ValidateBodyMatches(
        """
        x = "Line: " & Erl
        """,
        """
        x = "Line: " + Microsoft.VisualBasic.Information.Erl();
        """);

    [TestMethod]
    public void ErrRaiseCanonicalReRaise_StillCollapsesToThrow() => ValidateBodyMatches(
        """
        On Error GoTo handler
        x = 1
        handler:
        Err.Raise Err.Number, Err.Source, Err.Description, Err.HelpFile, Err.HelpContext
        """,
        """
        try
        {
            x = 1;
        }
        catch
        {
            throw;
        }
        """);

    [TestMethod]
    public void ErrRaiseAsNonStatementExpression_BecomesInformationErrCall() => ValidateBodyMatches(
        """
        y = LogAndRaise(Err.Raise(11, "mod.proc", "boom"))
        """,
        """
        y = LogAndRaise(Microsoft.VisualBasic.Information.Err().Raise(11, "mod.proc", "boom"));
        """);
}
