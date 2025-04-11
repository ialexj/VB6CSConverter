using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests;

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
}
