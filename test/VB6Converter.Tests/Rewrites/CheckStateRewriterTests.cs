using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

[TestClass]
public class CheckStateRewriterTests
{
    [TestMethod]
    public void VbChecked() => ValidateBodyMatches(
        "chk.Value = vbChecked",
        "chk.Value = CheckState.Checked;");

    [TestMethod]
    public void VbUnchecked() => ValidateBodyMatches(
        "chk.Value = vbUnchecked",
        "chk.Value = CheckState.Unchecked;");

    [TestMethod]
    public void VbGrayed() => ValidateBodyMatches(
        "chk.Value = vbGrayed",
        "chk.Value = CheckState.Indeterminate;");
}
