using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using VB6Converter.Rewriters;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class UnneededReturnRewriterTests
{
    static string RewriteMethodBody(string methodSignature, string body)
    {
        var input = CSharpSyntaxTree.ParseText(
            $$"""
            class C
            {
                {{methodSignature}}
                {
                    {{body}}
                }
            }
            """).GetCompilationUnitRoot();

        var output = (CompilationUnitSyntax)UnneededReturnRewriter.Default.Visit(input);
        var method = output.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return string.Join("\n", method.Body!.Statements.Select(s => s.NormalizeWhitespace().ToFullString()));
    }

    [TestMethod]
    public void ReturnAfterGoto_IsRemoved()
    {
        var body = RewriteMethodBody("void M()", "goto L1;\nreturn;\nL1:\nx = 1;");

        body.Should().Contain("goto L1;");
        body.Should().NotContain("return;");
        body.Should().Contain("L1:");
        body.Should().Contain("x = 1;");
    }

    [TestMethod]
    public void ReturnAfterThrow_IsRemoved()
    {
        var body = RewriteMethodBody("void M()", """
            throw new System.Exception("boom");
            return;
            """);

        body.Should().Contain("throw new System.Exception(\"boom\");");
        body.Should().NotContain("return;");
    }

    [TestMethod]
    public void NonImmediateReturnAfterGoto_IsRetained()
    {
        var body = RewriteMethodBody("void M()", "goto L1;\nx = 1;\nreturn;\nL1:\ny = 2;");

        body.Should().Contain("return;");
    }

    [TestMethod]
    public void UnreachableNonReturnStatements_AreRetained()
    {
        var body = RewriteMethodBody("void M()", """
            throw new System.Exception("boom");
            x = 1;
            return;
            """);

        body.Should().Contain("x = 1;");
        body.Should().Contain("return;");
    }

    [TestMethod]
    public void ReturnValueAfterGoto_IsRemoved()
    {
        var body = RewriteMethodBody("int M()", "goto L1;\nreturn x;\nL1:\nreturn 1;");

        body.Should().Contain("goto L1;");
        body.Should().NotContain("return x;");
        body.Should().Contain("L1:");
        body.Should().Contain("return 1;");
    }

    [TestMethod]
    public void BreakAfterGoto_IsRemoved()
    {
        var body = RewriteMethodBody("void M()", "while (true) { goto L1; break; L1: return; }");

        body.Should().Contain("goto L1;");
        body.Should().NotContain("goto L1;\n        break;");
    }

    [TestMethod]
    public void BreakAfterThrow_IsRemoved()
    {
        var body = RewriteMethodBody("void M()", "while (true) { throw new System.Exception(\"boom\"); break; }");

        body.Should().Contain("throw new System.Exception(\"boom\");");
        body.Should().NotContain("throw new System.Exception(\"boom\");\n        break;");
    }

    [TestMethod]
    public void BreakAfterReturn_IsRemoved()
    {
        var body = RewriteMethodBody("void M()", "while (true) { return; break; }");

        body.Should().Contain("return;");
        body.Should().NotContain("return;\n        break;");
    }

    [TestMethod]
    public void NonImmediateBreakAfterGoto_IsRetained()
    {
        var body = RewriteMethodBody("void M()", "while (true) { goto L1; x = 1; break; L1: return; }");

        body.Should().Contain("x = 1;");
        body.Should().Contain("break;");
    }
}
