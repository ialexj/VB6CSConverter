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

        [TestMethod]
        public void Replace() => ValidateBodyMatches(
            """
            x = Replace$(s, "a", "b");
            """,
            """
            x = ((string)s).Replace("a", "b");
            """
        );

        [TestMethod]
        public void Replace2() => ValidateMemberMatches(
            """
            Function SqlStr(ByVal s As String)
                SqlStr = "'" & Replace$(s, "'", "''") & "'"
            End Function
            """,
            """
            private static object SqlStr(string s) => "'" + ((string)s).Replace("'", "''") + "'";
            """);

        [TestMethod]
        public void IsNull() => ValidateBodyMatches(
            "x = IsNull(z)",
            "x = (z is null);");

    }

}
