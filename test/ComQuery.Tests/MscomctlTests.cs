using System.Linq;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector"/> against MSCOMCTL.OCX
/// (Microsoft Windows Common Controls 6.0).
/// GUID: {831FDD16-0C5C-11D2-A9FC-0000F8754DA1}  version 2.0
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class MscomctlTests : TypeLibraryInspectorIntegrationTestBase
{
    const string MscomctlPath = @"C:\Windows\SysWOW64\MSCOMCTL.OCX";
    static readonly Guid MscomctlGuid = new("831FDD16-0C5C-11D2-A9FC-0000F8754DA1");

    // stdole2 GUID — referenced by MSCOMCTL dependency tests
    static readonly Guid Stdole2Guid = new("00020430-0000-0000-C000-000000000046");

    [TestMethod]
    public void Mscomctl_Inspect_ReturnsNonNullModel()
    {
        if (!File.Exists(MscomctlPath)) Assert.Inconclusive("MSCOMCTL.OCX not found — skipping");

        var reference = MakeReference(MscomctlGuid, 2, 0, "Microsoft Windows Common Controls 6.0 (SP6)", MscomctlPath);
        var model = TypeLibraryInspector.Inspect(reference, MscomctlPath);

        model.Should().NotBeNull();
    }

    [TestMethod]
    public void Mscomctl_Inspect_ContainsWellKnownTypes()
    {
        if (!File.Exists(MscomctlPath)) Assert.Inconclusive("MSCOMCTL.OCX not found — skipping");

        var reference = MakeReference(MscomctlGuid, 2, 0, "Microsoft Windows Common Controls 6.0 (SP6)", MscomctlPath);
        var model = TypeLibraryInspector.Inspect(reference, MscomctlPath)!;

        var typeNames = model.Types.Select(t => t.Name).ToList();

        // These controls are well-known members of MSCOMCTL
        typeNames.Should().Contain("ListView",  "MSCOMCTL exposes a ListView control");
        typeNames.Should().Contain("TreeView",  "MSCOMCTL exposes a TreeView control");
        typeNames.Should().Contain("ImageList", "MSCOMCTL exposes an ImageList control");
    }

    [TestMethod]
    public void Mscomctl_Inspect_ModelGuidMatches()
    {
        if (!File.Exists(MscomctlPath)) Assert.Inconclusive("MSCOMCTL.OCX not found — skipping");

        var reference = MakeReference(MscomctlGuid, 2, 0, "Microsoft Windows Common Controls 6.0 (SP6)", MscomctlPath);
        var model = TypeLibraryInspector.Inspect(reference, MscomctlPath)!;

        model.Guid.Should().Be(MscomctlGuid);
    }

    [TestMethod]
    public void Mscomctl_Generate_CreatesListViewStubWithMembers()
    {
        if (!File.Exists(MscomctlPath)) Assert.Inconclusive("MSCOMCTL.OCX not found — skipping");

        var reference = MakeReference(MscomctlGuid, 2, 0, "Microsoft Windows Common Controls 6.0 (SP6)", MscomctlPath);
        var model = TypeLibraryInspector.Inspect(reference, MscomctlPath)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            var files = ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            files.Should().NotBeEmpty();

            // Every generated file should be valid C# (contain a namespace declaration)
            foreach (var file in files) {
                var text = File.ReadAllText(file);
                text.Should().Contain("namespace ", $"{Path.GetFileName(file)} must have a namespace");
            }

            // The ListView stub must exist
            var listViewFile = files.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals("ListView", StringComparison.OrdinalIgnoreCase));
            listViewFile.Should().NotBeNull("a ListView.cs stub should be generated");

            var lvText = File.ReadAllText(listViewFile!);
            // ListView should have at least one member (e.g. a property or method)
            lvText.Should().MatchRegex(@"(void|object|string|int|bool)\s+\w+\s*[({]",
                "ListView stub should have at least one typed member");
        }
        finally {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [TestMethod]
    public void Mscomctl_Generate_StubsAreUnderSafeNameSubfolder()
    {
        if (!File.Exists(MscomctlPath)) Assert.Inconclusive("MSCOMCTL.OCX not found — skipping");

        var reference = MakeReference(MscomctlGuid, 2, 0, "Microsoft Windows Common Controls 6.0 (SP6)", MscomctlPath);
        var model = TypeLibraryInspector.Inspect(reference, MscomctlPath)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            var files = ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            // All files should live under outDir/<SafeName>/
            var safeName = ComStubGenerator.ReferenceNaming.MakeSafeName(model.Name);
            var expectedSubdir = Path.Combine(outDir, safeName);
            files.All(f => f.StartsWith(expectedSubdir, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"all stubs must be under the safe-name subfolder '{safeName}'");
        }
        finally {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [TestMethod]
    public void Mscomctl_Inspect_DiscoversDependencyOnStdole2()
    {
        if (!File.Exists(MscomctlPath)) Assert.Inconclusive("MSCOMCTL.OCX not found — skipping");

        var reference = MakeReference(MscomctlGuid, 2, 0, "Microsoft Windows Common Controls 6.0 (SP6)", MscomctlPath);
        var model = TypeLibraryInspector.Inspect(reference, MscomctlPath)!;

        // MSCOMCTL uses OLE types from stdole2, so its discovered deps should include stdole2's GUID.
        model.DiscoveredDependencies.Should().NotBeNull();
        model.DiscoveredDependencies.Should().Contain(
            d => d.Guid == Stdole2Guid,
            "MSCOMCTL uses stdole2 types so stdole2 must appear in discovered dependencies");
    }
}
