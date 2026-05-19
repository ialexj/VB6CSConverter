using AwesomeAssertions;

namespace VB6Parser.Tests;

[TestClass]
public class ProjectReferenceTests
{
    static readonly Guid ImplicitVb6RuntimeGuid = new("000204EF-0000-0000-C000-000000000046");

    static VisualBasicProject LoadFromLines(string vbpContent)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.vbp");
        try {
            File.WriteAllText(tempFile, vbpContent);
            return VisualBasicProject.Load(tempFile);
        }
        finally {
            File.Delete(tempFile);
        }
    }

    // -----------------------------------------------------------------------
    // Reference= (TypeLibrary) lines
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Reference_StandardFormat_ParsedCorrectly()
    {
        const string vbp = "Reference=*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\\Windows\\system32\\stdole2.tlb#OLE Automation";

        var project = LoadFromLines(vbp);

        project.References.Should().Contain(r => r.Guid == ImplicitVb6RuntimeGuid);
        var r = project.References.Single(r => r.Guid == new Guid("00020430-0000-0000-C000-000000000046"));
        r.Kind.Should().Be(ProjectReferenceKind.TypeLibrary);
        r.Guid.Should().Be(new Guid("00020430-0000-0000-C000-000000000046"));
        r.MajorVersion.Should().Be(2);
        r.MinorVersion.Should().Be(0);
        r.Lcid.Should().Be(0);
        r.Description.Should().Be("OLE Automation");
        r.DeclaredPath.Should().Be(@"C:\Windows\system32\stdole2.tlb");
    }

    [TestMethod]
    public void Reference_ScriptingRuntime_ParsedCorrectly()
    {
        const string vbp = "Reference=*\\G{420B2830-E718-11CF-893D-00A0C9054228}#1.0#0#C:\\Windows\\system32\\scrrun.dll#Microsoft Scripting Runtime";

        var project = LoadFromLines(vbp);

        var r = project.References.Single(r => r.Guid == new Guid("420B2830-E718-11CF-893D-00A0C9054228"));
        r.Guid.Should().Be(new Guid("420B2830-E718-11CF-893D-00A0C9054228"));
        r.MajorVersion.Should().Be(1);
        r.MinorVersion.Should().Be(0);
        r.Description.Should().Be("Microsoft Scripting Runtime");
    }

    [TestMethod]
    public void Reference_VersionWithMultipleDigits_ParsedCorrectly()
    {
        const string vbp = "Reference=*\\G{00062FFF-0000-0000-C000-000000000046}#12.5#0#C:\\path\\to\\lib.dll#Some Library";

        var project = LoadFromLines(vbp);

        var r = project.References.Single(r => r.Guid == new Guid("00062FFF-0000-0000-C000-000000000046"));
        r.MajorVersion.Should().Be(12);
        r.MinorVersion.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Object= (ActiveX/OCX) lines
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Object_StandardFormat_ParsedCorrectly()
    {
        const string vbp = "Object={F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0; COMDLG32.OCX";

        var project = LoadFromLines(vbp);

        var r = project.References.Single(r => r.Guid == new Guid("F9043C88-F6F2-101A-A3C9-08002B2F49FB"));
        r.Kind.Should().Be(ProjectReferenceKind.ActiveX);
        r.Guid.Should().Be(new Guid("F9043C88-F6F2-101A-A3C9-08002B2F49FB"));
        r.MajorVersion.Should().Be(1);
        r.MinorVersion.Should().Be(2);
        r.Lcid.Should().Be(0);
        r.DeclaredPath.Should().Be("COMDLG32.OCX");
    }

    [TestMethod]
    public void Object_WithNoSpaceAfterSemicolon_ParsedCorrectly()
    {
        const string vbp = "Object={86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}#1.2#0;mscomct2.ocx";

        var project = LoadFromLines(vbp);

        var r = project.References.Single(r => r.Guid == new Guid("86CF1D34-0C5F-11D2-A9FC-0000F8754DA1"));
        r.Kind.Should().Be(ProjectReferenceKind.ActiveX);
        r.DeclaredPath.Should().Be("mscomct2.ocx");
    }

    // -----------------------------------------------------------------------
    // Mixed VBP with both files and references
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MixedVbp_FilesAndReferences_BothParsed()
    {
        const string vbp = """
            Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\system32\stdole2.tlb#OLE Automation
            Object={F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0; COMDLG32.OCX
            Form=Form1.frm
            Module=Module1; Module1.bas
            """;

        var tempDir  = Path.Combine(Path.GetTempPath(), $"vbptest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "test.vbp");
        try {
            // Create placeholder source files so path resolution doesn't fail
            File.WriteAllText(Path.Combine(tempDir, "Form1.frm"), "");
            File.WriteAllText(Path.Combine(tempDir, "Module1.bas"), "");
            File.WriteAllText(tempFile, vbp);

            var project = VisualBasicProject.Load(tempFile);

            project.Files.Should().HaveCount(2);
            project.References.Should().HaveCount(3);
            project.References.Should().Contain(r => r.Guid == ImplicitVb6RuntimeGuid);
            project.References.Should().Contain(r => r.Kind == ProjectReferenceKind.TypeLibrary
                && r.Guid == new Guid("00020430-0000-0000-C000-000000000046"));
            project.References.Should().Contain(r => r.Kind == ProjectReferenceKind.ActiveX
                && r.Guid == new Guid("F9043C88-F6F2-101A-A3C9-08002B2F49FB"));
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // Malformed / edge-case lines
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MalformedReference_NoHash_SkippedWithoutThrowing()
    {
        const string vbp = "Reference=NOTAVALIDENTRY";

        var project = LoadFromLines(vbp);

        project.References.Should().HaveCount(2);
        project.References.Should().Contain(r => r.Guid == ImplicitVb6RuntimeGuid);
        project.References.Should().Contain(r => r.Guid == ImplicitStdOleGuid);
    }

    [TestMethod]
    public void MalformedReference_InvalidGuid_SkippedWithoutThrowing()
    {
        const string vbp = "Reference=*\\GNOTGUID#2.0#0#C:\\path#Desc";

        var project = LoadFromLines(vbp);

        project.References.Should().HaveCount(2);
        project.References.Should().Contain(r => r.Guid == ImplicitVb6RuntimeGuid);
        project.References.Should().Contain(r => r.Guid == ImplicitStdOleGuid);
    }

    [TestMethod]
    public void UnknownVbpLine_Ignored()
    {
        const string vbp = """
            Type=Exe
            Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\system32\stdole2.tlb#OLE Automation
            Startup="Sub Main"
            """;

        var project = LoadFromLines(vbp);

        // One explicit reference parsed + implicit VB6 runtime
        project.References.Should().HaveCount(2);
        project.References.Should().Contain(r => r.Guid == new Guid("00020430-0000-0000-C000-000000000046"));
        project.References.Should().Contain(r => r.Guid == ImplicitVb6RuntimeGuid);
    }

    [TestMethod]
    public void ImplicitVb6Runtime_ExplicitlyPresent_NotDuplicated()
    {
        const string vbp = "Reference=*\\G{000204EF-0000-0000-C000-000000000046}#6.0#9#C:\\Windows\\SysWOW64\\MSVBVM60.DLL#Visual Basic For Applications";

        var project = LoadFromLines(vbp);

        project.References.Count(r => r.Guid == ImplicitVb6RuntimeGuid && r.MajorVersion == 6 && r.MinorVersion == 0)
            .Should().Be(1);
    }

    static readonly Guid ImplicitStdOleGuid = new("00020430-0000-0000-C000-000000000046");

    [TestMethod]
    public void ImplicitStdOle_AddedWhenAbsent()
    {
        var project = LoadFromLines(string.Empty);

        project.References.Should().Contain(r => r.Guid == ImplicitStdOleGuid);
    }

    [TestMethod]
    public void ImplicitStdOle_ExplicitlyPresent_NotDuplicated()
    {
        const string vbp = "Reference=*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\\Windows\\SysWOW64\\stdole2.tlb#OLE Automation";

        var project = LoadFromLines(vbp);

        project.References.Count(r => r.Guid == ImplicitStdOleGuid && r.MajorVersion == 2 && r.MinorVersion == 0)
            .Should().Be(1);
    }
}
