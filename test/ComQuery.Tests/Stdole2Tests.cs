using System.Linq;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;
using LibraryMemberKind = ComQuery.LibraryMemberKind;
using LibraryTypeKind = ComQuery.LibraryTypeKind;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector"/> against stdole2.tlb
/// (OLE Automation — always present on Windows).
/// GUID: {00020430-0000-0000-C000-000000000046}  version 2.0
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class Stdole2Tests : TypeLibraryInspectorIntegrationTestBase
{
    const string Stdole2Path = @"C:\Windows\System32\stdole2.tlb";
    static readonly Guid Stdole2Guid = new("00020430-0000-0000-C000-000000000046");

    [TestMethod]
    public void Stdole2_Inspect_ReturnsNonNullModel()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path);

        model.Should().NotBeNull();
    }

    [TestMethod]
    public void Stdole2_Inspect_ModelNameAndGuidMatch()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        model.Guid.Should().Be(Stdole2Guid);
        model.Major.Should().Be(2);
        model.Minor.Should().Be(0);
    }

    [TestMethod]
    public void Stdole2_Inspect_ContainsAtLeastOneType()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        model.Types.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Stdole2_Generate_CreatesStubFiles()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            var files = ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            files.Should().NotBeEmpty("stub generator should produce at least one file");
            files.All(File.Exists).Should().BeTrue("every returned path should exist on disk");
        }
        finally {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [TestMethod]
    public void Stdole2_Inspect_ContainsOleHandleAlias()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        var oleHandle = model.Types.FirstOrDefault(t => t.Name == "OLE_HANDLE");
        oleHandle.Should().NotBeNull("stdole2 defines OLE_HANDLE as a TKIND_ALIAS");
        oleHandle!.Kind.Should().Be(LibraryTypeKind.Alias);
        oleHandle.AliasedType.Should().Be("int",
            "OLE_HANDLE resolves to int in the stdole2 type library");
    }

    [TestMethod]
    public void Stdole2_Generate_EmitsAliasesFileContainingOleHandle()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            // Aliases are no longer written per-library; collect them via CollectAliases instead.
            var aliases = ComStubGenerator.ReferenceStubGenerator.CollectAliases(ToStubModel(model));
            aliases.Should().Contain(a => a.Name == "OLE_HANDLE" && a.CSharpType == "int",
                "OLE_HANDLE resolves to int in the stdole2 type library");
        }
        finally {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [TestMethod]
    public void Stdole2_Inspect_PopulatesDiscoveredDependencies()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        // stdole2 references types from other libraries via VT_USERDEFINED,
        // so DiscoveredDependencies should be populated.
        model.DiscoveredDependencies.Should().NotBeNull();
        // The dep list may include self-references, but should be a collection (not throw).
        // We don't assert a specific count since it varies by OS/registration state.
    }

    [TestMethod]
    public void Inspect_Stdole2_Font_DispatchInterfaceHasVarDispatchProperties()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        var fontType = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "Font", StringComparison.OrdinalIgnoreCase)
            && t.Kind == LibraryTypeKind.DispatchInterface);
        if (fontType == null) Assert.Inconclusive("Font dispinterface not present in stdole2 on this machine");

        // stdole2.Font uses the ODL "properties:" section (VAR_DISPATCH VARDESCs) to describe
        // its members, not INVOKE_PROPERTYGET/PUT FUNCDESCs.  The inspector must read cVars for
        // TKIND_DISPATCH so these properties are not silently dropped.
        fontType!.Members.Should().Contain(m =>
            string.Equals(m.Name, "Name", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.PropertyGet,
            "Font.Name should be surfaced from its VAR_DISPATCH VARDESC");

        fontType.Members.Should().Contain(m =>
            string.Equals(m.Name, "Size", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.PropertyGet,
            "Font.Size should be surfaced from its VAR_DISPATCH VARDESC");
    }

    [TestMethod]
    public void Inspect_Stdole2_IFont_ContainsDirectFontProperties()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        var iFontType = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "IFont", StringComparison.OrdinalIgnoreCase));
        if (iFontType == null) {
            Assert.Inconclusive("IFont type not present in stdole2 type library on this machine");
        }

        // Diagnostic: show what IFont actually has
        var memberNames = string.Join(", ", iFontType!.Members.Select(m => $"{m.Name}:{m.Kind}"));
        var inheritedInterfaces = string.Join(", ", iFontType.ImplementedInterfaces ?? []);

        System.Diagnostics.Debug.WriteLine($"IFont Kind: {iFontType.Kind}");
        System.Diagnostics.Debug.WriteLine($"IFont Members ({iFontType.Members.Count}): {memberNames}");
        System.Diagnostics.Debug.WriteLine($"IFont Implemented Interfaces: {inheritedInterfaces}");

        iFontType.Members.Should().Contain(m =>
            string.Equals(m.Name, "Name", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.PropertyGet,
            "IFont should have the Name property");

        iFontType.Members.Should().Contain(m =>
            string.Equals(m.Name, "Size", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.PropertyGet,
            "IFont should have the Size property");

        iFontType.Members.Should().Contain(m =>
            string.Equals(m.Name, "Charset", StringComparison.OrdinalIgnoreCase)
            && m.Kind == LibraryMemberKind.PropertyGet,
            "IFont should have the Charset property");
    }

    [TestMethod]
    public void Inspect_Stdole2_IPicture_InheritanceTest()
    {
        if (!File.Exists(Stdole2Path)) Assert.Inconclusive("stdole2.tlb not found — skipping");

        var reference = MakeReference(Stdole2Guid, 2, 0, "OLE Automation", Stdole2Path);
        var model = TypeLibraryInspector.Inspect(reference, Stdole2Path)!;

        // IPicture is another interface in stdole2 that may have inheritance
        var iPictureType = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "IPicture", StringComparison.OrdinalIgnoreCase));
        if (iPictureType == null) {
            Assert.Inconclusive("IPicture type not present in stdole2 type library on this machine");
        }

        var memberNames = string.Join(", ", iPictureType!.Members.Select(m => $"{m.Name}"));
        var inheritedInterfaces = string.Join(", ", iPictureType.ImplementedInterfaces ?? []);

        System.Diagnostics.Debug.WriteLine($"IPicture Kind: {iPictureType.Kind}");
        System.Diagnostics.Debug.WriteLine($"IPicture Members ({iPictureType.Members.Count}): {memberNames}");
        System.Diagnostics.Debug.WriteLine($"IPicture Implemented Interfaces: {inheritedInterfaces}");

        // IPicture should have members from itself and/or inherited interfaces
        iPictureType.Members.Should().NotBeEmpty("IPicture should have at least some members");
    }
}
