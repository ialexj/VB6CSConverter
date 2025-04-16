using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Tests
{
    [TestClass]
    public class CompilationTests
    {
        [TestMethod]
        public void Compilation()
        {
            var cu1 = VB6ToCSharpConversion.ConvertString("""
                Public Sub HelloWorld1()
                End Sub
                """, 
                "HelloWorld", "Test", VB6Parser.VisualBasicFileType.Module);

            var cu2 = VB6ToCSharpConversion.ConvertString("""
                Public Sub HelloWorld2()
                End Sub
                """,
                "HelloWorld", "Test", VB6Parser.VisualBasicFileType.Module);

            var comp = VB6ToCSharpConversion.GetCompilation([cu1.CompilationUnit, cu2.CompilationUnit]);

            var ms = new MemoryStream();
            var result = comp.Emit(ms);
            result.Success.Should().BeTrue();
        }

    }
}
