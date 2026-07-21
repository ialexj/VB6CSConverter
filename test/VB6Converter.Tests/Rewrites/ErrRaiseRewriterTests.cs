using VB6Converter.Rewriters;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class ErrRaiseRewriterTests
{
    [TestMethod]
    public void ErrRaise_UsesDescriptionAndMapsData() => ValidateBodyMatches(
        """
        Err.Raise Erro_NaoEncontrado, "clsDocClienteHeader.VendasCarrega", "A venda não foi encontrada."
        """,
        """
        throw new System.Exception("A venda não foi encontrada.")
        {
            Data =
            {
                ["Code"] = Erro_NaoEncontrado,
                ["Source"] = "clsDocClienteHeader.VendasCarrega"
            }
        };
        """, new ErrRaiseRewriter());

    [TestMethod]
    public void ErrRaise_MapsHelpFieldsWhenProvided() => ValidateBodyMatches(
        """
        Err.Raise 11, "mod.proc", "boom", "help.chm", 42
        """,
        """
        throw new System.Exception("boom")
        {
            Data =
            {
                ["Code"] = 11,
                ["Source"] = "mod.proc",
                ["HelpFile"] = "help.chm",
                ["HelpContext"] = 42
            }
        };
        """, new ErrRaiseRewriter());

    [TestMethod]
    public void ErrRaise_WithoutDescription_UsesEmptyString() => ValidateBodyMatches(
        """
        Err.Raise 11, "mod.proc"
        """,
        """
        throw new System.Exception("")
        {
            Data =
            {
                ["Code"] = 11,
                ["Source"] = "mod.proc"
            }
        };
        """, new ErrRaiseRewriter());

    [TestMethod]
    public void ErrRaise_WithCanonicalErrArguments_BecomesThrow() => ValidateBodyMatches(
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
        """, new ErrRaiseRewriter());
}
