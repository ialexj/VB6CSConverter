using AwesomeAssertions;
using VB6Parser;

namespace VB6Converter.Tests;

[TestClass]
public class ConversionTargetTests
{
    [TestMethod]
    public void Create_RootLevelFile_UsesOutputRoot()
    {
        var file = new VisualBasicProjectFile(
            Path.Combine("C:\\", "Project", "Main.bas"),
            "Main",
            VisualBasicFileType.Module);

        var target = ConversionTarget.Create(file, Path.Combine("C:\\", "Out"), Path.Combine("C:\\", "Project"));

        target.OutputPath.Should().Be(Path.Combine("C:\\", "Out", "Main.cs"));
    }

    [TestMethod]
    public void Create_NestedFile_PreservesRelativeFolderStructure()
    {
        var file = new VisualBasicProjectFile(
            Path.Combine("C:\\", "Project", "Forms", "MainForm.frm"),
            "MainForm",
            VisualBasicFileType.Form);

        var target = ConversionTarget.Create(file, Path.Combine("C:\\", "Out"), Path.Combine("C:\\", "Project"));

        target.OutputPath.Should().Be(Path.Combine("C:\\", "Out", "Forms", "MainForm.cs"));
    }

    [TestMethod]
    public void Create_MultiLevelNestedFile_PreservesAllFolders()
    {
        var file = new VisualBasicProjectFile(
            Path.Combine("C:\\", "Project", "Forms", "Dialogs", "Settings.frm"),
            "Settings",
            VisualBasicFileType.Form);

        var target = ConversionTarget.Create(file, Path.Combine("C:\\", "Out"), Path.Combine("C:\\", "Project"));

        target.OutputPath.Should().Be(Path.Combine("C:\\", "Out", "Forms", "Dialogs", "Settings.cs"));
    }

    [TestMethod]
    public void Create_FileOutsideProjectRoot_FlattensToOutputRoot()
    {
        var file = new VisualBasicProjectFile(
            Path.Combine("C:\\", "Shared", "Common.bas"),
            "Common",
            VisualBasicFileType.Module);

        var target = ConversionTarget.Create(file, Path.Combine("C:\\", "Out"), Path.Combine("C:\\", "Project"));

        target.OutputPath.Should().Be(Path.Combine("C:\\", "Out", "Common.cs"));
    }
}