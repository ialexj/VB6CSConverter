using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;
using static VB6Converter.Tests.Validations;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class IOTests
{
    [TestMethod]
    public void OpenReadModeUsesFileOpenWithInputMode()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileOpen");
        body.Should().Contain("OpenMode.Input");
    }

    [TestMethod]
    public void OpenOutputModeUsesFileOpenWithOutputMode()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Output As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileOpen");
        body.Should().Contain("OpenMode.Output");
    }

    [TestMethod]
    public void OpenAppendModeUsesFileOpenWithAppendMode()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Append As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileOpen");
        body.Should().Contain("OpenMode.Append");
    }

    [TestMethod]
    public void OpenInputSharedUsesFileOpenWithSharedShare()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input Shared As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("OpenMode.Input");
        body.Should().Contain("OpenShare.Shared");
    }

    [TestMethod]
    public void OpenWithAccessReadUsesOpenAccessRead()
    {
        var conversion = ConvertBody(
            """
            Open "a.txt" For Input Access Read As #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("OpenAccess.Read");
    }

    [TestMethod]
    public void PrintStatementUsesPrintLine()
    {
        var conversion = ConvertBody(
            """
            Print #1, x
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.PrintLine");
    }

    [TestMethod]
    public void PrintWithTrailingSemicolonUsesPrint()
    {
        var conversion = ConvertBody(
            """
            Print #1, x;
            """);

        GetBodyText(conversion).Should().Contain("Microsoft.VisualBasic.FileSystem.Print(");
        GetBodyText(conversion).Should().NotContain("PrintLine");
    }

    [TestMethod]
    public void PrintSpcDoesNotProduceError()
    {
        var conversion = ConvertBody(
            """
            Print #1, SPC(3)
            """);

        conversion.TransformErrors.Should().BeEmpty();
        GetBodyText(conversion).Should().Contain("SPC(");
    }

    [TestMethod]
    public void PrintTabDoesNotProduceError()
    {
        var conversion = ConvertBody(
            """
            Print #1, TAB(5)
            """);

        conversion.TransformErrors.Should().BeEmpty();
        GetBodyText(conversion).Should().Contain("TAB(");
    }

    [TestMethod]
    public void CloseStatementUsesFileClose()
    {
        var conversion = ConvertBody(
            """
            Close #1
            """);

        GetBodyText(conversion).Should().Contain("Microsoft.VisualBasic.FileSystem.FileClose");
    }

    [TestMethod]
    public void CloseWithNoArgsUsesFileCloseWithNoArgs()
    {
        var conversion = ConvertBody(
            """
            Close
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileClose()");
    }

    [TestMethod]
    public void CloseWithMultipleFileNumbersPassesAll()
    {
        var conversion = ConvertBody(
            """
            Dim f1 As Integer
            Dim f2 As Integer
            Close f1, f2
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("FileClose");
        body.Should().Contain("f1");
        body.Should().Contain("f2");
    }

    [TestMethod]
    public void LineInputStatementAssignsReturnValue()
    {
        var conversion = ConvertBody(
            """
            Line Input #1, lineText
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.LineInput(");
        body.Should().Contain("lineText");
        body.Should().Contain("=");
    }

    [TestMethod]
    public void WriteStatementPassesFileNumAndItems()
    {
        var conversion = ConvertBody(
            """
            Write #1, x, y
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Write(");
        body.Should().Contain("1");
    }

    [TestMethod]
    public void InputStatementUsesRefArguments()
    {
        var conversion = ConvertBody(
            """
            Input #1, x, y
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Input(");
        body.Should().Contain("ref");
    }

    [TestMethod]
    public void InputStatementProducesOneCallPerVariable()
    {
        var conversion = ConvertBody(
            """
            Input #1, x, y
            """);

        var method = conversion.Class.Members.OfType<MethodDeclarationSyntax>().Single();
        method.Body!.Statements.Should().HaveCount(2);
    }

    [TestMethod]
    public void PutStatementUsesFilePut()
    {
        var conversion = ConvertBody(
            """
            Put #1, , x
            """);

        GetBodyText(conversion).Should().Contain("Microsoft.VisualBasic.FileSystem.FilePut(");
    }

    [TestMethod]
    public void GetStatementUsesFileGet()
    {
        var conversion = ConvertBody(
            """
            Get #1, , x
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.FileGet(");
        body.Should().Contain("ref");
    }

    [TestMethod]
    public void SeekStatementUsesSeek()
    {
        var conversion = ConvertBody(
            """
            Seek #1, 100
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Seek(");
        body.Should().Contain("100");
    }

    [TestMethod]
    public void KillStatementUsesKill()
    {
        var conversion = ConvertBody(
            """
            Kill filePath
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Kill(");
    }


    [TestMethod]
    public void FileCopyStatementUsesFileSystemFileCopy()
    {
        var conversion = ConvertBody(
            """
            FileCopy "source.txt", "dest.txt"
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.FileCopy");
    }

    [TestMethod]
    public void NameStatementUsesFileSystemRename()
    {
        var conversion = ConvertBody(
            """
            Name "old.txt" As "new.txt"
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Rename");
    }

    [TestMethod]
    public void ResetStatementUsesFileSystemReset()
    {
        var conversion = ConvertBody(
            """
            Reset
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Reset");
    }

    [TestMethod]
    public void WidthStatementUsesFileSystemFileWidth()
    {
        var conversion = ConvertBody(
            """
            Width #1, 80
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.FileWidth");
        body.Should().Contain("1");
        body.Should().Contain("80");
    }

    [TestMethod]
    public void MkDirStatementUsesFileSystemMkDir()
    {
        var conversion = ConvertBody(
            """
            MkDir "C:\newdir"
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.MkDir");
    }

    [TestMethod]
    public void RmDirStatementUsesFileSystemRmDir()
    {
        var conversion = ConvertBody(
            """
            RmDir "C:\olddir"
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.RmDir");
    }

    [TestMethod]
    public void ChDirStatementUsesFileSystemChDir()
    {
        var conversion = ConvertBody(
            """
            ChDir "C:\newdir"
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.ChDir");
    }

    [TestMethod]
    public void ChDriveStatementUsesFileSystemChDrive()
    {
        var conversion = ConvertBody(
            """
            ChDrive "D"
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.ChDrive");
    }

    [TestMethod]
    public void SetAttrStatementUsesFileSystemSetAttr()
    {
        var conversion = ConvertBody(
            """
            SetAttr "file.txt", 1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.SetAttr");
    }

    [TestMethod]
    public void LockStatementUsesFileSystemLock()
    {
        var conversion = ConvertBody(
            """
            Lock #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Lock");
        body.Should().Contain("1");
    }

    [TestMethod]
    public void LockStatementWithRangePassesThreeArgs()
    {
        var conversion = ConvertBody(
            """
            Lock #1, 1 To 5
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Lock");
        body.Should().Contain("1");
        body.Should().Contain("5");
    }

    [TestMethod]
    public void UnlockStatementUsesFileSystemUnlock()
    {
        var conversion = ConvertBody(
            """
            Unlock #1
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Unlock");
    }

    [TestMethod]
    public void EofFunctionUsesFileSystemEof()
    {
        var conversion = ConvertBody(
            """
            Dim x As Boolean
            x = EOF(1)
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.EOF");
    }

    [TestMethod]
    public void LofFunctionUsesFileSystemLof()
    {
        var conversion = ConvertBody(
            """
            Dim x As Long
            x = LOF(1)
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.LOF");
    }

    [TestMethod]
    public void FreeFileFunctionUsesFileSystemFreeFile()
    {
        var conversion = ConvertBody(
            """
            Dim f As Integer
            f = FreeFile()
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.FreeFile");
    }

    [TestMethod]
    public void DirFunctionUsesFileSystemDir()
    {
        var conversion = ConvertBody(
            """
            Dim s As String
            s = Dir("C:\*.*")
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Dir");
    }

    [TestMethod]
    public void SeekFunctionUsesFileSystemSeek()
    {
        var conversion = ConvertBody(
            """
            Dim pos As Long
            pos = Seek(1)
            """);

        var body = GetBodyText(conversion);
        body.Should().Contain("Microsoft.VisualBasic.FileSystem.Seek");
    }

    static VB6ToCSharpConversion ConvertBody(string vb, string? name = null)
    {
        var wrapper = $"""
        Sub Test()
            {vb}
        End Sub
        """;

        var conversion = VB6ToCSharpConversion.ConvertString(wrapper, name ?? nameof(IOTests));
        conversion.ParseErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();
        return conversion;
    }

    static string GetBodyText(VB6ToCSharpConversion conversion)
    {
        var method = conversion.Class.Members.OfType<MethodDeclarationSyntax>().Single();
        var sb = new StringBuilder();

        foreach (var statement in method.Body!.Statements) {
            sb.AppendLine(statement.NormalizeWhitespace().ToFullString());
        }

        return sb.ToString();
    }
}
