using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector.InspectAll"/> against ActBar.ocx.
/// Verifies that both the VB6-facing OCA library ("ActiveBarLibraryCtl") and the
/// automation OCX library ("ActiveBarLibrary") are returned when the companion .oca exists.
/// GUID: {0F987290-56EE-11D0-9C43-00A0C90F29FC}  version 1.0
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class ActBarTests : TypeLibraryInspectorIntegrationTestBase
{
    const string ActBarPath = @"C:\WINDOWS\SysWow64\ActBar.ocx";
    static readonly Guid ActBarGuid = new("0F987290-56EE-11D0-9C43-00A0C90F29FC");

    [TestMethod]
    public void ActBar_InspectAll_ReturnsBothOcaAndOcxLibraries()
    {
        if (!File.Exists(ActBarPath)) Assert.Inconclusive("ActBar.ocx not found — skipping");

        string ocaPath = Path.ChangeExtension(ActBarPath, ".oca");
        if (!File.Exists(ocaPath)) Assert.Inconclusive("ActBar.oca not found — skipping (OCA companion required for this test)");

        var results = TypeLibraryInspector.InspectAll(ActBarGuid, 1, 0, "ActBar", ActBarPath);

        results.Should().Contain(r => r.Name == "ActiveBarLibraryCtl",
            "ActBar.oca must expose the VB6-facing library name 'ActiveBarLibraryCtl'");
        results.Should().Contain(r => r.Name == "ActiveBarLibrary",
            "ActBar.ocx must expose the automation library name 'ActiveBarLibrary'");
    }

    [TestMethod]
    public void ActBar_InspectAll_BothLibrariesHaveTypes()
    {
        if (!File.Exists(ActBarPath)) Assert.Inconclusive("ActBar.ocx not found — skipping");

        string ocaPath = Path.ChangeExtension(ActBarPath, ".oca");
        if (!File.Exists(ocaPath)) Assert.Inconclusive("ActBar.oca not found — skipping");

        var results = TypeLibraryInspector.InspectAll(ActBarGuid, 1, 0, "ActBar", ActBarPath);

        foreach (var lib in results) {
            lib.Types.Should().NotBeEmpty($"library '{lib.Name}' must contain at least one type");
        }
    }
}
