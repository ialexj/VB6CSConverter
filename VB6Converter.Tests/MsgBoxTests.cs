using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests
{
    [TestClass]
    public class MsgBoxTests
    {
        [TestMethod]
        public void MsgBox() => ValidateBodyMatches(
            """
            MsgBox "This is a test!", vbOkOnly + vbInformation, "Cópia Periódica"
            """,
            """
            MessageBox.Show("This is a test!", "Cópia Periódica", MessageBoxButtons.OK, MessageBoxIcon.Information);
            """);

    }
}
