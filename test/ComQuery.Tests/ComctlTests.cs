using System.Linq;
using System.Runtime.Versioning;
using AwesomeAssertions;
using ComQuery;

namespace ComQuery.Tests;

/// <summary>
/// Integration tests for <see cref="TypeLibraryInspector"/> against comctl32.ocx
/// (Microsoft Windows Common Controls — ComctlLib).
/// GUID: {6b7e6392-850a-101b-afc0-4210102a8da7}  version 1.0
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public class ComctlTests : TypeLibraryInspectorIntegrationTestBase
{
    const string ComctlPath = @"C:\WINDOWS\SysWow64\comctl32.Ocx";
    static readonly Guid ComctlGuid = new("6b7e6392-850a-101b-afc0-4210102a8da7");

    [TestMethod]
    public void ComctlLib_Inspect_DumpCollectionTypes()
    {
        // Diagnostic: writes every member of collection types to %TEMP%\ComctlLib_Dump.txt
        // so we can see which members produce duplicate Item/this[] combinations.
        if (!File.Exists(ComctlPath)) Assert.Inconclusive("comctl32.Ocx not found — skipping");

        var reference = MakeReference(ComctlGuid, 1, 0, "Microsoft Windows Common Controls", ComctlPath);
        var model = TypeLibraryInspector.Inspect(reference, ComctlPath)!;

        model.Should().NotBeNull("comctl32.Ocx must be loadable");

        var dumpPath = Path.Combine(Path.GetTempPath(), "ComctlLib_Dump.txt");
        using (var sw = new StreamWriter(dumpPath, append: false)) {
            foreach (var type in model.Types.OrderBy(t => t.Name)) {
                sw.WriteLine($"Type: {type.Name} ({type.Kind})");
                foreach (var m in type.Members ?? []) {
                    sw.WriteLine(
                        $"  {m.Kind,-15} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"))}) " +
                        $"-> {m.ReturnType}  IsDefault={m.IsDefault}");
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"ComctlLib dump written to: {dumpPath}");
        model.Types.Should().NotBeEmpty();
    }

    [TestMethod]
    public void ComctlLib_Generate_ColumnHeaders_NoItemPropertyWhenIndexerPresent()
    {
        if (!File.Exists(ComctlPath)) Assert.Inconclusive("comctl32.Ocx not found — skipping");

        var reference = MakeReference(ComctlGuid, 1, 0, "Microsoft Windows Common Controls", ComctlPath);
        var model = TypeLibraryInspector.Inspect(reference, ComctlPath)!;

        var outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try {
            ComStubGenerator.ReferenceStubGenerator.Generate(ToStubModel(model), outDir);

            var file = Directory.GetFiles(outDir, "ColumnHeaders.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            file.Should().NotBeNull("ColumnHeaders.cs should be generated");

            var source = File.ReadAllText(file!);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "ComctlLib_ColumnHeaders.cs"), source);

            source.Should().Contain("this[", "ColumnHeaders must have an indexer");
            source.Should().NotContain("ColumnHeader Item",
                "a named Item property must not appear alongside this[]");
        }
        finally {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
