#nullable enable
using AwesomeAssertions;

namespace VB6Parser.Tests;

[TestClass]
public class ProjectInfoTests
{
    static readonly string VersionInfoBlock = """
        Type=Exe
        Form=Form1; Form1.frm
        Name="MyProject"
        MajorVer=2
        MinorVer=3
        RevisionVersion=42
        Title="My App"
        VersionComments="Built by hand"
        VersionCompanyName="Acme"
        VersionFileDescription="Does things"
        VersionLegalCopyright="(c) Acme"
        VersionLegalTrademarks="TM"
        VersionProductName="Widget"
        """;

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

    [TestMethod]
    public void Version_ParsedFromVbp()
    {
        var project = LoadFromLines(VersionInfoBlock);

        project.Version.Should().Be(new VisualBasicProjectVersion(2, 3, 42));
    }

    [TestMethod]
    public void Version_Partial_IsFilledWithZeros()
    {
        var project = LoadFromLines("Type=Exe\r\nMajorVer=5\r\n");

        project.Version.Should().Be(new VisualBasicProjectVersion(5, 0, 0));
    }

    [TestMethod]
    public void Version_Absent_IsNull()
    {
        var project = LoadFromLines("Type=Exe\r\nName=\"MyProject\"\r\n");

        project.Version.Should().BeNull();
    }

    [TestMethod]
    public void Metadata_ParsedAndUnquoted()
    {
        var project = LoadFromLines(VersionInfoBlock);

        project.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> {
            ["title"] = "My App",
            ["comments"] = "Built by hand",
            ["companyName"] = "Acme",
            ["fileDescription"] = "Does things",
            ["legalCopyright"] = "(c) Acme",
            ["legalTrademarks"] = "TM",
            ["productName"] = "Widget",
        });
    }

    [TestMethod]
    public void Metadata_EmptyValues_AreOmitted()
    {
        var project = LoadFromLines("Type=Exe\r\nTitle=\"\"\r\nVersionComments=\"real\"\r\n");

        project.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> {
            ["comments"] = "real",
        });
    }

    [TestMethod]
    public void Metadata_Absent_IsEmpty()
    {
        var project = LoadFromLines("Type=Exe\r\nName=\"MyProject\"\r\n");

        project.Metadata.Should().BeEmpty();
    }
}
