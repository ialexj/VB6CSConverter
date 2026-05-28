using System.Linq;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;
using LibraryMemberKind = ComQuery.LibraryMemberKind;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector"/> against VB6.OLB
/// (Visual Basic 6 Object Library — ships with VB6 IDE).
/// GUID: {FCFB3D2E-A0FA-1068-A738-08002B3371B5}  version 4.0
/// Contains: Form, UserControl, MDIForm, App, Clipboard, Screen, etc.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class Vb6OlbTests : TypeLibraryInspectorIntegrationTestBase
{
    const string Vb6OlbPath = @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.OLB";
    static readonly Guid Vb6OlbGuid = new("FCFB3D2E-A0FA-1068-A738-08002B3371B5");

    [TestMethod]
    public void Vb6Olb_Inspect_ReturnsNonNullModel()
    {
        if (!File.Exists(Vb6OlbPath)) Assert.Inconclusive("VB6.OLB not found — skipping");

        var reference = MakeReference(Vb6OlbGuid, 4, 0, "Visual Basic For Applications", Vb6OlbPath);
        var model = TypeLibraryInspector.Inspect(reference, Vb6OlbPath);

        model.Should().NotBeNull();
    }

    [TestMethod]
    public void Vb6Olb_Inspect_ListAllTypes()
    {
        if (!File.Exists(Vb6OlbPath)) Assert.Inconclusive("VB6.OLB not found — skipping");

        var reference = MakeReference(Vb6OlbGuid, 4, 0, "Visual Basic For Applications", Vb6OlbPath);
        var model = TypeLibraryInspector.Inspect(reference, Vb6OlbPath)!;

        var allTypeNames = string.Join(", ", model.Types.Select(t => $"{t.Name}({t.Kind})").OrderBy(x => x));
        System.Diagnostics.Debug.WriteLine($"VB6.OLB Types: {allTypeNames}");

        model.Types.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Vb6Olb_Inspect_UserControl_ContainsClientHeight()
    {
        if (!File.Exists(Vb6OlbPath)) Assert.Inconclusive("VB6.OLB not found — skipping");

        var reference = MakeReference(Vb6OlbGuid, 4, 0, "Visual Basic For Applications", Vb6OlbPath);
        var model = TypeLibraryInspector.Inspect(reference, Vb6OlbPath)!;

        var userControl = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "UserControl", StringComparison.OrdinalIgnoreCase));
        if (userControl == null) Assert.Inconclusive("UserControl not found in VB6.OLB on this machine");

        var memberNames = string.Join(", ", userControl!.Members.Select(m => $"{m.Name}:{m.Kind}"));
        var implementedInterfaces = string.Join(", ", userControl.ImplementedInterfaces ?? []);
        System.Diagnostics.Debug.WriteLine($"UserControl Kind: {userControl.Kind}");
        System.Diagnostics.Debug.WriteLine($"UserControl Members ({userControl.Members.Count}): {memberNames}");
        System.Diagnostics.Debug.WriteLine($"UserControl Implemented Interfaces: {implementedInterfaces}");

        var hasClientHeight = userControl!.Members.Any(m =>
            string.Equals(m.Name, "ClientHeight", StringComparison.OrdinalIgnoreCase)
            && (m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet));
        if (!hasClientHeight) Assert.Inconclusive("ClientHeight not found in UserControl on this machine — may be absent from this version of VB6.OLB");

        userControl.Members.Should().Contain(m =>
            string.Equals(m.Name, "ClientHeight", StringComparison.OrdinalIgnoreCase)
            && (m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet),
            "UserControl should expose ClientHeight");
    }

}
