using System.Linq;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector"/> against DAO type libraries
/// (Microsoft DAO 3.5 Object Library and ACEDAO).
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class DaoTests : TypeLibraryInspectorIntegrationTestBase
{
    // DAO 3.5  (Microsoft DAO Object Library — ships with Office/VB6)
    const string DaoPath = @"C:\Program Files (x86)\Common Files\Microsoft Shared\DAO\DAO2535.TLB";
    static readonly Guid DaoGuid = new("00025E01-0000-0000-C000-000000000046");

    // ACEDAO — newer Office/Access DAO (ACE engine)
    const string AceDaoPath = @"C:\Program Files\Microsoft Office\root\VFS\ProgramFilesCommonX64\Microsoft Shared\Office16\ACEDAO.DLL";
    static readonly Guid AceDaoGuid = new("4ac9e1da-5bad-4ac7-86e3-24f4cdceca28");

    [TestMethod]
    public void DAO_Inspect_RecordsetMembersWithDefaultFlag()
    {
        if (!File.Exists(DaoPath)) Assert.Inconclusive("DAO2535.TLB not found — skipping");

        var reference = MakeReference(DaoGuid, 3, 5, "Microsoft DAO 3.5 Object Library", DaoPath);
        var model = TypeLibraryInspector.Inspect(reference, DaoPath)!;

        // Dump all Recordset-related types and their default members for diagnostics
        foreach (var type in model.Types.Where(t =>
            t.Name.IndexOf("Recordset", StringComparison.OrdinalIgnoreCase) >= 0)) {
            System.Diagnostics.Debug.WriteLine($"Type: {type.Name} ({type.Kind})");
            foreach (var m in type.Members ?? []) {
                System.Diagnostics.Debug.WriteLine(
                    $"  {m.Kind} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"))}) " +
                    $"-> {m.ReturnType}  IsDefault={m.IsDefault}");
            }
        }

        // DAO Recordset must exist
        var recordset = model.Types.FirstOrDefault(t =>
            string.Equals(t.Name, "Recordset", StringComparison.OrdinalIgnoreCase));
        recordset.Should().NotBeNull("DAO library must contain a Recordset type");

        // Recordset's DISPID 0 is Fields (no params) — emitted as named property, not indexer
        var fieldsDefault = recordset!.Members.FirstOrDefault(m => m.IsDefault);
        fieldsDefault.Should().NotBeNull("Recordset must have a default member (DISPID 0)");
        fieldsDefault!.Parameters.Should().BeEmpty(
            "Recordset's DISPID 0 (Fields) has no parameters; forwarding indexer is emitted instead");
    }

    [TestMethod]
    public void DAO_Generate_Recordset_HasForwardingIndexer()
    {
        if (!File.Exists(DaoPath)) Assert.Inconclusive("DAO2535.TLB not found — skipping");

        var reference = MakeReference(DaoGuid, 3, 5, "Microsoft DAO 3.5 Object Library", DaoPath);
        var model = TypeLibraryInspector.Inspect(reference, DaoPath)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            var recordsetFile = Directory.GetFiles(outDir, "Recordset.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            recordsetFile.Should().NotBeNull("a Recordset.cs stub should be generated");

            var source = File.ReadAllText(recordsetFile!);
            System.Diagnostics.Debug.WriteLine("=== Recordset.cs ===");
            System.Diagnostics.Debug.WriteLine(source);

            // Must expose this[] so that rs!MyField → rs["MyField"] compiles
            source.Should().Contain("this[",
                "Recordset stub needs a forwarding this[] indexer for rs!Field → rs[\"Field\"] support");
            // The regular Fields property must also still be present
            source.Should().Contain("Fields",
                "Recordset stub must still expose the named Fields property");
        }
        finally {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [TestMethod]
    public void AceDAO_Inspect_DumpRecordsetAndFieldsTypes()
    {
        // Diagnostic test: dumps every member of every Recordset/Fields-related type
        // in ACEDAO.DLL so we can see what kinds and DISPID 0 assignments exist.
        if (!File.Exists(AceDaoPath)) Assert.Inconclusive("ACEDAO.DLL not found — skipping");

        var reference = MakeReference(AceDaoGuid, 12, 0, "Microsoft Office 16.0 Access Database Engine Object Library", AceDaoPath);
        var model = TypeLibraryInspector.Inspect(reference, AceDaoPath)!;

        model.Should().NotBeNull("ACEDAO.DLL must be loadable");

        var dumpPath = Path.Combine(Path.GetTempPath(), "AceDAO_Recordset_Dump.txt");
        using (var sw = new System.IO.StreamWriter(dumpPath, append: false)) {
            foreach (var type in model.Types.Where(t =>
                t.Name.IndexOf("Recordset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.Name.IndexOf("Fields",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.Name.IndexOf("Field",     StringComparison.OrdinalIgnoreCase) >= 0)) {
                sw.WriteLine($"Type: {type.Name} ({type.Kind})");
                foreach (var m in type.Members ?? []) {
                    sw.WriteLine(
                        $"  {m.Kind,-15} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"))}) " +
                        $"-> {m.ReturnType}  IsDefault={m.IsDefault}");
                }
            }
        }
        System.Diagnostics.Debug.WriteLine($"Dump written to: {dumpPath}");

        // This test always passes — read the dump file for diagnostics.
        model.Types.Should().NotBeEmpty();
    }

    [TestMethod]
    public void AceDAO_Generate_Recordset_HasForwardingIndexer()
    {
        if (!File.Exists(AceDaoPath)) Assert.Inconclusive("ACEDAO.DLL not found — skipping");

        var reference = MakeReference(AceDaoGuid, 12, 0, "Microsoft Office 16.0 Access Database Engine Object Library", AceDaoPath);
        var model = TypeLibraryInspector.Inspect(reference, AceDaoPath)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            var recordsetFile = Directory.GetFiles(outDir, "Recordset.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            recordsetFile.Should().NotBeNull("a Recordset.cs stub should be generated");

            var source = File.ReadAllText(recordsetFile!);
            // Write to a known path for manual inspection
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "AceDAO_Recordset.cs"), source);

            source.Should().Contain("this[",
                "Recordset stub needs a this[] indexer for rs!Field → rs[\"Field\"] support");
        }
        finally {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
