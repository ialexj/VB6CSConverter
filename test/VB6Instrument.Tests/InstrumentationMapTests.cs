#nullable enable
using AwesomeAssertions;
using VB6Parser;

namespace VB6Instrument.Tests;

[TestClass]
public class InstrumentationMapTests
{
    /// <summary>Normalizes line endings: raw string literals pick up the
    /// source file's endings, and the JSON writer's newline style must not
    /// leak into assertions.</summary>
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static MapMember Member(MapFile file, string key) =>
        file.Members.Single(member => member.Key == key);

    private static InstrumentationMap MapWith(string key, MapFile file)
    {
        var map = new InstrumentationMap { ProjectName = "Test.vbp" };
        map.AddFile(key, file);
        return map;
    }

    [TestMethod]
    public void MemberRangeTypeAndLineMapAreCaptured()
    {
        var (_, result) = TestSources.RunLineNumbersMapped(
            """
            Attribute VB_Name = "modMap"
            Option Explicit

            Public Sub DoWork()
                Dim x As Long
                x = 1
                If x > 0 Then
                    x = 2
                End If
            End Sub
            """,
            "modMap.bas");

        Lf(MapWith("modMap.bas", result.FileMap).ToJson()).Should().Be(Lf(
"""
{
  "project": {
    "name": "Test.vbp"
  },
  "files": {
    "modMap.bas": {
      "name": "modMap",
      "type": "Module",
      "members": {
        "DoWork": {
          "type": "Sub",
          "start": 4,
          "end": 10,
          "length": 7,
          "lines": {
            "10": 6,
            "20": 7,
            "30": 8,
            "40": 9
          }
        }
      }
    }
  }
}
"""));
    }

    [TestMethod]
    public void PropertyAccessorsGetSuffixedKeys()
    {
        var (_, result) = TestSources.RunLineNumbersMapped(
            """
            Public Property Get Thing() As Long
                Thing = value
            End Property

            Public Property Let Thing(ByVal rhs As Long)
                value = rhs
            End Property

            Public Sub Flip()
                Thing = Thing + 1
            End Sub
            """,
            "clsMap.cls");

        var file = result.FileMap;
        file.Type.Should().Be("Class");
        Member(file, "Thing.Get").Type.Should().Be("Property Get");
        Member(file, "Thing.Let").Type.Should().Be("Property Let");
        Member(file, "Flip").Type.Should().Be("Sub");
        file.Members.Select(member => member.Key).Should().ContainSingle(key => !key.Contains('.'));
    }

    [TestMethod]
    public void AlreadyNumberedMemberIsMappedToo()
    {
        // A member that already carries numbers (e.g. from the incumbent
        // tool) is skipped by pass A — its existing Erl numbers are still
        // part of the file's virtual-line state and must be mapped.
        var (_, result) = TestSources.RunLineNumbersMapped(
            """
            Public Sub Foo()
            10        x = 1
            20        x = 2
            End Sub
            """);

        result.MembersSkipped.Should().Be(1);
        var lines = Member(result.FileMap, "Foo").Lines;
        lines[(10)].Should().Be(2);
        lines[(20)].Should().Be(3);
    }

    [TestMethod]
    public void FirstCaseAndUnnumberedLinesAreAbsent()
    {
        var (_, result) = TestSources.RunLineNumbersMapped(
            """
            Public Sub Pick(ByVal code As Long)
                Select Case code
                    Case 1
                        code = 1
                    Case 2
                        code = 2
                End Select
            End Sub
            """);

        var lines = Member(result.FileMap, "Pick").Lines;
        lines.Keys.Should().BeEquivalentTo([10, 20, 30, 40, 50]);
        lines.Should().Contain(new Dictionary<int, int> {
            [10] = 2,   // Select Case
            [20] = 4,   // code = 1
            [30] = 5,   // Case 2 (numbered)
            [40] = 6,   // code = 2
            [50] = 7,   // End Select
        });
    }

    [TestMethod]
    public void MultiLineStatementMapsToItsFirstLine()
    {
        var (_, result) = TestSources.RunLineNumbersMapped(
            """
            Public Sub Foo()
                Dim text As String
                text = _
                    "value"
                text = "other"
            End Sub
            """);

        // The continuation-folded statement starts on physical line 3.
        var lines = Member(result.FileMap, "Foo").Lines;
        lines[(10)].Should().Be(3);
        lines[(20)].Should().Be(5);
    }

    [TestMethod]
    public void RealLinesStayOriginalWhenLaterPassesInsertLines()
    {
        // The call-trace pass inserts a line before the numbered statement;
        // the map must keep pointing at the original (pre-instrumentation)
        // line — the canonical source the developer reads.
        LineNumbersResult? result = null;
        var output = TestSources.Run(
            """
            Attribute VB_Name = "modMap"
            Public Sub DoWork()
                x = 1
            End Sub
            """,
            [
                model => result = new LineNumbersPass().Run(model),
                model => new CallTracePass("Debug.Write \"{File}.{Member}\"").Run(model),
            ],
            "modMap.bas");

        var lines = output.Split("\r\n");
        lines[2].Should().Contain("Debug.Write");    // inserted trace now on line 3
        lines[3].Should().StartWith("10");           // numbered statement shifted to line 4
        Member(result!.FileMap, "DoWork").Lines[(10)].Should().Be(3);
    }

    [TestMethod]
    public void VbNameIsReadFromTheFileAttribute()
    {
        var (_, withName) = TestSources.RunLineNumbersMapped(
            """
            Attribute VB_Name = "modReal"
            Public Sub Foo()
                x = 1
            End Sub
            """,
            "modFile.bas");
        withName.FileMap.Name.Should().Be("modReal");

        var (_, withoutName) = TestSources.RunLineNumbersMapped(
            """
            Public Sub Foo()
                x = 1
            End Sub
            """);
        withoutName.FileMap.Name.Should().BeNull();
    }

    [TestMethod]
    public void DeclaredFileTypeWinsOverExtension()
    {
        // For .vbp targets the parser-declared type is passed through; the
        // extension only serves as fallback.
        LineNumbersResult? result = null;
        TestSources.Run(
            """
            Attribute VB_Name = "modMap"
            Public Sub Foo()
                x = 1
            End Sub
            """,
            [model => result = new LineNumbersPass("Control").Run(model)],
            "modMap.bas");

        result!.FileMap.Type.Should().Be("Control");
    }

    [TestMethod]
    public void FileTypeDerivesFromExtension()
    {
        MapFile.FileTypeFromExtension(".bas").Should().Be("Module");
        MapFile.FileTypeFromExtension(".CLS").Should().Be("Class");
        MapFile.FileTypeFromExtension(".frm").Should().Be("Form");
        MapFile.FileTypeFromExtension(".ctl").Should().Be("Control");
        MapFile.FileTypeFromExtension(".dob").Should().Be("UserDocument");
        MapFile.FileTypeFromExtension(".dsr").Should().Be("ActiveXDesigner");
        MapFile.FileTypeFromExtension(".pag").Should().Be("PropertyPage");
        MapFile.FileTypeFromExtension(".txt").Should().Be("Module");
    }

    [TestMethod]
    public void ProjectSectionAndFileOrderAreRendered()
    {
        var (_, zeta) = TestSources.RunLineNumbersMapped(
            """
            Attribute VB_Name = "zeta"
            Public Sub Go()
                x = 1
            End Sub
            """,
            "zeta.bas");
        var (_, alpha) = TestSources.RunLineNumbersMapped(
            """
            Attribute VB_Name = "alpha"
            Public Sub Click()
                x = 1
            End Sub
            """,
            "alpha.frm");

        var map = new InstrumentationMap {
            ProjectName = "MyProject.vbp",
            Version = new VisualBasicProjectVersion(2, 3, 42),
            Metadata = new Dictionary<string, string> {
                ["title"] = "My App",
                ["fileDescription"] = "Does things",
            },
        };
        map.AddFile("zeta.bas", zeta.FileMap);
        map.AddFile("alpha.frm", alpha.FileMap);

        Lf(map.ToJson()).Should().Be(Lf(
"""
{
  "project": {
    "name": "MyProject.vbp",
    "version": {
      "major": 2,
      "minor": 3,
      "revision": 42
    },
    "metadata": {
      "title": "My App",
      "fileDescription": "Does things"
    }
  },
  "files": {
    "alpha.frm": {
      "name": "alpha",
      "type": "Form",
      "members": {
        "Click": {
          "type": "Sub",
          "start": 2,
          "end": 4,
          "length": 3,
          "lines": {
            "10": 3
          }
        }
      }
    },
    "zeta.bas": {
      "name": "zeta",
      "type": "Module",
      "members": {
        "Go": {
          "type": "Sub",
          "start": 2,
          "end": 4,
          "length": 3,
          "lines": {
            "10": 3
          }
        }
      }
    }
  }
}
"""));
    }

    [TestMethod]
    public void MemberKeySuffixesOnlyProperties()
    {
        MapFile.MemberKey(new Vb6MemberRange("CloseSale", "Sub", "Sub", "Public ", 0, 0, 4))
            .Should().Be("CloseSale");
        MapFile.MemberKey(new Vb6MemberRange("Compute", "Function", "Function", "Private ", 0, 0, 4))
            .Should().Be("Compute");
        MapFile.MemberKey(new Vb6MemberRange("Thing", "Property Get", "Property", "Public ", 0, 0, 4))
            .Should().Be("Thing.Get");
        MapFile.MemberKey(new Vb6MemberRange("Thing", "Property Let", "Property", "Public ", 0, 0, 4))
            .Should().Be("Thing.Let");
        MapFile.MemberKey(new Vb6MemberRange("Thing", "Property Set", "Property", "Public ", 0, 0, 4))
            .Should().Be("Thing.Set");
        // Casing of the source is canonicalized in the suffix.
        MapFile.MemberKey(new Vb6MemberRange("Thing", "property get", "property", "Public ", 0, 0, 4))
            .Should().Be("Thing.Get");
    }

    [TestMethod]
    public void MapPathFollowsTargetShape()
    {
        InstrumentationMap.GetMapPath(@"C:\Build\Staged\Optiware\Optiware.vbp")
            .Should().Be(@"C:\Build\Staged\Optiware\Optiware.vbp.map");
        InstrumentationMap.GetMapPath(@"C:\Build\Staged\Optiware\modMain.bas")
            .Should().Be(@"C:\Build\Staged\Optiware\modMain.bas.map");

        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-maptests", "StagedTree");
        Directory.CreateDirectory(directory);
        try {
            InstrumentationMap.GetMapPath(directory).Should().Be(Path.Combine(directory, "StagedTree.map"));
        }
        finally {
            Directory.Delete(directory);
        }
    }
}
