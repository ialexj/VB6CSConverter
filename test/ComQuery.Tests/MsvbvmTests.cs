using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;
using LibraryMemberKind = ComQuery.LibraryMemberKind;
using LibraryTypeKind = ComQuery.LibraryTypeKind;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector"/> against MSVBVM60.DLL
/// sub-libraries and the VBA library.
/// <para>
/// MSVBVM60.DLL embeds multiple type-library resources.  The one at resource ID 3
/// is registered in HKCR\TypeLib with a path that includes the resource-ID suffix
/// ("...MSVBVM60.DLL\3").  Prior to the fix, File.Exists rejected such paths and
/// ResolveTypeLibPath returned null, so no stubs were ever generated for VB / VBRUN types.
/// </para>
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class MsvbvmTests : TypeLibraryInspectorIntegrationTestBase
{
    const string MsvbvmPath = @"C:\WINDOWS\SysWow64\MSVBVM60.DLL";

    // Registered in HKCR\TypeLib as "Visual Basic runtime objects and procedures",
    // path = MSVBVM60.DLL\3.
    static readonly Guid VbRuntimeSubLibGuid = new("EA544A21-C82D-11D1-A3E4-00A0C90AEA82");

    // VBA library ("Visual Basic For Applications") — contains Collection, ErrObject, etc.
    // path = MSVBVM60.DLL (or VBE7.DLL on newer machines).
    static readonly Guid VbaLibGuid = new("000204EF-0000-0000-C000-000000000046");

    // ──────────────────────────────────────────────────────────────────────
    // IsTypeLibPath
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsTypeLibPath_ResourceIdSuffix_ReturnsTrueWhenBaseExists()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        TypeLibraryInspector.IsTypeLibPath(MsvbvmPath + @"\3").Should().BeTrue(
            "a DLL\\N path is a valid type library path when the base DLL exists — " +
            "LoadTypeLib accepts this format natively");
    }

    [TestMethod]
    public void IsTypeLibPath_PlainDll_ReturnsTrue()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        TypeLibraryInspector.IsTypeLibPath(MsvbvmPath).Should().BeTrue(
            "a plain DLL path should be accepted");
    }

    [TestMethod]
    public void IsTypeLibPath_NonExistentFile_ReturnsFalse()
    {
        TypeLibraryInspector.IsTypeLibPath(@"C:\does\not\exist.dll").Should().BeFalse();
    }

    [TestMethod]
    public void IsTypeLibPath_NonExistentFileWithResourceId_ReturnsFalse()
    {
        TypeLibraryInspector.IsTypeLibPath(@"C:\does\not\exist.dll\2").Should().BeFalse();
    }

    [TestMethod]
    public void IsTypeLibPath_Null_ReturnsFalse()
    {
        TypeLibraryInspector.IsTypeLibPath(null).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // VB runtime sub-library (resource ID 3)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveTypeLibPath_VbRuntimeSubLib_ReturnsResourceIdPath()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = TypeLibraryInspector.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);

        path.Should().NotBeNull(
            $"GUID {{{VbRuntimeSubLibGuid}}} is registered in HKCR\\TypeLib with path " +
            "'MSVBVM60.DLL\\3' — it must be resolvable now that IsTypeLibPath handles resource-ID suffixes");
        path!.Should().EndWith(@"\3",
            "the registered path for this sub-library ends with the resource-ID suffix \\3");
        path.ToUpperInvariant().Should().Contain("MSVBVM60.DLL",
            "the VB runtime sub-library lives inside MSVBVM60.DLL");
    }

    [TestMethod]
    public void Inspect_VbRuntimeSubLib_ReturnsNonNullModel()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = TypeLibraryInspector.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbRuntimeSubLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbRuntimeSubLibGuid, 6, 0, "VB runtime", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!);

        model.Should().NotBeNull(
            "TypeLibraryInspector must be able to load the sub-library from its resource-ID path");
    }

    [TestMethod]
    public void Inspect_VbRuntimeSubLib_ContainsTypes()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = TypeLibraryInspector.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbRuntimeSubLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbRuntimeSubLibGuid, 6, 0, "VB runtime", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!)!;

        model.Types.Should().NotBeEmpty(
            "the VB runtime sub-library exposes multiple types (enums, interfaces, classes)");
    }

    [TestMethod]
    public void Inspect_VbRuntimeSubLib_PropertyBag_ImplementsInterfaces()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = TypeLibraryInspector.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbRuntimeSubLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbRuntimeSubLibGuid, 6, 0, "VB runtime", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!)!;

        // PropertyBag is the only TKIND_COCLASS in MSVBVM60.DLL\3.
        // It implements _PropertyBag (a dispatch interface that carries Read/Write/Contents).
        var propertyBag = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "PropertyBag", StringComparison.OrdinalIgnoreCase)
            && t.Kind == LibraryTypeKind.Class);
        propertyBag.Should().NotBeNull("PropertyBag is always present in MSVBVM60.DLL\\3");

        propertyBag!.ImplementedInterfaces.Should().NotBeNull();
        propertyBag.ImplementedInterfaces!.Should().NotBeEmpty(
            "PropertyBag coclass implements _PropertyBag");
    }

    [TestMethod]
    public void Inspect_VbRuntimeSubLib_PropertyBag_ContainsInheritedMembers()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = TypeLibraryInspector.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbRuntimeSubLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbRuntimeSubLibGuid, 6, 0, "VB runtime", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!)!;

        // PropertyBag is the only TKIND_COCLASS in MSVBVM60.DLL\3.
        // Its _PropertyBag dispatch interface exposes ReadProperty, WriteProperty and Contents.
        // The coclass inspector must collect these members by walking implemented interfaces.
        var propertyBag = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "PropertyBag", StringComparison.OrdinalIgnoreCase)
            && t.Kind == LibraryTypeKind.Class);
        propertyBag.Should().NotBeNull("PropertyBag is always present in MSVBVM60.DLL\\3");

        propertyBag!.Members.Should().Contain(m =>
            string.Equals(m.Name, "ReadProperty", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.Method,
            "members inherited from the _PropertyBag dispatch interface must be collected for the coclass");

        propertyBag.Members.Should().Contain(m =>
            string.Equals(m.Name, "WriteProperty", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.Method,
            "members inherited from the _PropertyBag dispatch interface must be collected for the coclass");
    }

    [TestMethod]
    public void Generate_VbRuntimeSubLib_ProducesStubFiles()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = TypeLibraryInspector.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbRuntimeSubLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbRuntimeSubLibGuid, 6, 0, "VB runtime", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            var files = ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            files.Should().NotBeEmpty(
                "the VB runtime sub-library stub generator must produce at least one file");
            files.All(File.Exists).Should().BeTrue(
                "every returned path should exist on disk");
        }
        finally {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // VBA library — _NewEnum → IEnumerable promotion
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Inspect_VbaLib_Collection_ImplementsIEnumerable()
    {
        var path = TypeLibraryInspector.ResolveTypeLibPath(VbaLibGuid, 6, 0)
                ?? TypeLibraryInspector.ResolveTypeLibPath(VbaLibGuid, 5, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbaLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbaLibGuid, 6, 0, "VBA", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!)!;

        // The VB6 Collection class exposes _NewEnum at DISPID -4.
        // The inspector must replace it with GetEnumerator and inject IEnumerable.
        var collection = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "Collection", StringComparison.OrdinalIgnoreCase));
        if (collection == null) Assert.Inconclusive($"Collection type not found in VBA library at {path} on this machine");

        collection!.ImplementedInterfaces.Should().NotBeNull();
        collection.ImplementedInterfaces!.Should().Contain(
            "System.Collections.IEnumerable",
            "Collection exposes _NewEnum (DISPID -4); the inspector must inject IEnumerable");

        collection.Members.Should().Contain(m =>
            string.Equals(m.Name, "GetEnumerator", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.Method
            && m.ReturnType == "System.Collections.IEnumerator",
            "_NewEnum must be replaced with GetEnumerator returning IEnumerator");

        collection.Members.Should().NotContain(m =>
            string.Equals(m.Name, "_NewEnum", StringComparison.OrdinalIgnoreCase),
            "_NewEnum must not appear in the member list — it was replaced by GetEnumerator");
    }
}
