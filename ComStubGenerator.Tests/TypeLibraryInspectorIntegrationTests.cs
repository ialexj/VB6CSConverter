using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComStubGenerator;
using VB6Parser;

namespace ComStubGenerator.Tests;

/// <summary>
/// Live integration tests that exercise <see cref="TypeLibraryInspector"/> against
/// real COM type libraries that are expected to exist on a Windows developer machine.
/// These tests are intentionally skipped when the required files are absent.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class TypeLibraryInspectorIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────
    // stdole2  (OLE Automation — always present on Windows)
    // GUID: {00020430-0000-0000-C000-000000000046}  version 2.0
    // ──────────────────────────────────────────────────────────────────────

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
            var files = ReferenceStubGenerator.Generate(model, outDir);

            files.Should().NotBeEmpty("stub generator should produce at least one file");
            files.All(File.Exists).Should().BeTrue("every returned path should exist on disk");

            // Each file should contain [GeneratedCode]
            foreach (var file in files.Where(f => !Path.GetFileName(f).StartsWith("_"))) {
                File.ReadAllText(file).Should().Contain("GeneratedCode",
                    $"{Path.GetFileName(file)} should carry [GeneratedCode] attribute");
            }
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
        oleHandle.AliasedCSharpType.Should().Be("int",
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
            ReferenceStubGenerator.Generate(model, outDir);

            // Aliases are no longer written per-library; collect them via CollectAliases instead.
            var aliases = ReferenceStubGenerator.CollectAliases(model);
            aliases.Should().Contain(a => a.Name == "OLE_HANDLE" && a.CSharpType == "int",
                "OLE_HANDLE resolves to int in the stdole2 type library");
        }
        finally {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // MSCOMCTL.OCX  (Microsoft Windows Common Controls 6.0)
    // GUID: {831FDD16-0C5C-11D2-A9FC-0000F8754DA1}  version 2.0
    // ──────────────────────────────────────────────────────────────────────

    const string MscomctlPath = @"C:\Windows\SysWOW64\MSCOMCTL.OCX";
    static readonly Guid MscomctlGuid = new("831FDD16-0C5C-11D2-A9FC-0000F8754DA1");

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
            var files = ReferenceStubGenerator.Generate(model, outDir);

            files.Should().NotBeEmpty();

            // Every generated file should be valid C# (contain a namespace declaration)
            foreach (var file in files) {
                var text = File.ReadAllText(file);
                text.Should().Contain("namespace ", $"{Path.GetFileName(file)} must have a namespace");
                text.Should().Contain("GeneratedCode",
                    $"{Path.GetFileName(file)} must carry [GeneratedCode]");
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
            var files = ReferenceStubGenerator.Generate(model, outDir);

            // All files should live under outDir/<SafeName>/
            var expectedSubdir = Path.Combine(outDir, model.SafeName);
            files.All(f => f.StartsWith(expectedSubdir, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"all stubs must be under the safe-name subfolder '{model.SafeName}'");
        }
        finally {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
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

    // ──────────────────────────────────────────────────────────────────────
    // MSVBVM60.DLL sub-libraries (VB / VBRUN)
    //
    // MSVBVM60.DLL embeds multiple type-library resources.  The one at
    // resource ID 3 is registered in HKCR\TypeLib with a path that includes
    // the resource-ID suffix ("...MSVBVM60.DLL\3").  Prior to the fix,
    // File.Exists rejected such paths and ResolveTypeLibPath returned null,
    // so no stubs were ever generated for VB / VBRUN types.
    // ──────────────────────────────────────────────────────────────────────

    const string MsvbvmPath = @"C:\WINDOWS\SysWow64\MSVBVM60.DLL";

    // Registered in HKCR\TypeLib as "Visual Basic runtime objects and procedures",
    // path = MSVBVM60.DLL\3.
    static readonly Guid VbRuntimeSubLibGuid = new("EA544A21-C82D-11D1-A3E4-00A0C90AEA82");

    [TestMethod]
    public void IsTypeLibPath_ResourceIdSuffix_ReturnsTrueWhenBaseExists()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        VisualBasicProject.IsTypeLibPath(MsvbvmPath + @"\3").Should().BeTrue(
            "a DLL\\N path is a valid type library path when the base DLL exists — " +
            "LoadTypeLib accepts this format natively");
    }

    [TestMethod]
    public void IsTypeLibPath_PlainDll_ReturnsTrue()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        VisualBasicProject.IsTypeLibPath(MsvbvmPath).Should().BeTrue(
            "a plain DLL path should be accepted");
    }

    [TestMethod]
    public void IsTypeLibPath_NonExistentFile_ReturnsFalse()
    {
        VisualBasicProject.IsTypeLibPath(@"C:\does\not\exist.dll").Should().BeFalse();
    }

    [TestMethod]
    public void IsTypeLibPath_NonExistentFileWithResourceId_ReturnsFalse()
    {
        VisualBasicProject.IsTypeLibPath(@"C:\does\not\exist.dll\2").Should().BeFalse();
    }

    [TestMethod]
    public void IsTypeLibPath_Null_ReturnsFalse()
    {
        VisualBasicProject.IsTypeLibPath(null).Should().BeFalse();
    }

    [TestMethod]
    public void ResolveTypeLibPath_VbRuntimeSubLib_ReturnsResourceIdPath()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = VisualBasicProject.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);

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

        var path = VisualBasicProject.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
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

        var path = VisualBasicProject.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
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

        var path = VisualBasicProject.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
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

        var path = VisualBasicProject.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
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

    [TestMethod]
    public void Generate_VbRuntimeSubLib_ProducesStubFiles()
    {
        if (!File.Exists(MsvbvmPath)) Assert.Inconclusive("MSVBVM60.DLL not found — skipping");

        var path = VisualBasicProject.ResolveTypeLibPath(VbRuntimeSubLibGuid, 6, 0);
        if (path == null) Assert.Inconclusive($"GUID {{{VbRuntimeSubLibGuid}}} not registered — skipping");

        var reference = MakeReference(VbRuntimeSubLibGuid, 6, 0, "VB runtime", path!);
        var model = TypeLibraryInspector.Inspect(reference, path!)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            var files = ReferenceStubGenerator.Generate(model, outDir);

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
    // VB6.OLB  (Visual Basic 6 Object Library — ships with VB6 IDE)
    // GUID: {FCFB3D2E-A0FA-1068-A738-08002B3371B5}  version 4.0
    // Contains: Form, UserControl, MDIForm, App, Clipboard, Screen, etc.
    // ──────────────────────────────────────────────────────────────────────

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

        userControl.Members.Should().Contain(m =>
            string.Equals(m.Name, "ClientHeight", StringComparison.OrdinalIgnoreCase)
            && (m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet),
            "UserControl should expose ClientHeight");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static VisualBasicProjectReference MakeReference(
        Guid guid, int major, int minor, string description, string path) =>
        new(ProjectReferenceKind.ActiveX, guid, major, minor, 0, description,
            DeclaredPath: path, ResolvedPath: path);
}
