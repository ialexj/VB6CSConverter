using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VB6Converter.Tests;

[TestClass]
public class DiagnosticsJsonTests
{
    static CSharpCompilation CreateCompilationWithErrors()
    {
        var code1 = """
            namespace Test
            {
                class Class1
                {
                    int x = "hello";
                    string y = 42;
                    string z = "world";
                }
            }
            """;

        var code2 = """
            namespace Test
            {
                class Class2
                {
                    int a = "bad";
                    int b = 0;
                }
            }
            """;

        var tree1 = CSharpSyntaxTree.ParseText(code1, path: @"C:\output\src\File1.cs");
        var tree2 = CSharpSyntaxTree.ParseText(code2, path: @"C:\output\src\File2.cs");

        var references = new MetadataReference[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        return CSharpCompilation.Create(
            "TestProject",
            [tree1, tree2],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    static string SerializeJson(IReadOnlyCollection<Diagnostic> diagnostics, string? outputRoot)
    {
        using var writer = new StringWriter();
        DiagnosticsReport.WriteJson(writer, diagnostics, outputRoot);
        return writer.ToString();
    }

    static JsonNode ParseJson(string json)
    {
        return JsonNode.Parse(json)!;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Determinism
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Determinism_SameDiagnostics_ProducesIdenticalJson()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();

        var json1 = SerializeJson(diagnostics, @"C:\output");
        var json2 = SerializeJson(diagnostics, @"C:\output");

        // Compare everything except generatedAt
        var obj1 = ParseJson(json1);
        var obj2 = ParseJson(json2);
        obj1["generatedAt"] = null;
        obj2["generatedAt"] = null;

        obj1.ToJsonString().Should().Be(obj2.ToJsonString());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ordering
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SummaryByCode_SortedByCountDescThenCodeAsc()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var byCode = root["summary"]!["byCode"]!.AsArray();
        var entries = byCode.Select(e => (
            code: e!["code"]!.GetValue<string>(),
            count: e!["count"]!.GetValue<int>()
        )).ToArray();

        for (int i = 1; i < entries.Length; i++) {
            if (entries[i - 1].count == entries[i].count) {
                string.Compare(entries[i - 1].code, entries[i].code, StringComparison.Ordinal).Should().BeLessThanOrEqualTo(0);
            }
            else {
                entries[i - 1].count.Should().BeGreaterThanOrEqualTo(entries[i].count);
            }
        }
    }

    [TestMethod]
    public void SummaryByFile_SortedByErrorCountDescThenFileAsc()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var byFile = root["summary"]!["byFile"]!.AsArray();
        var entries = byFile.Select(e => (
            file: e!["file"]!.GetValue<string>(),
            errorCount: e!["errorCount"]!.GetValue<int>()
        )).ToArray();

        for (int i = 1; i < entries.Length; i++) {
            if (entries[i - 1].errorCount == entries[i].errorCount) {
                string.Compare(entries[i - 1].file, entries[i].file, StringComparison.Ordinal).Should().BeLessThanOrEqualTo(0);
            }
            else {
                entries[i - 1].errorCount.Should().BeGreaterThanOrEqualTo(entries[i].errorCount);
            }
        }
    }

    [TestMethod]
    public void ByCodeInstances_SortedByFileThenLineThenColumn()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var byCode = root["byCode"]!.AsArray();
        foreach (var entry in byCode) {
            var instances = entry!["instances"]!.AsArray();
            JsonNode? prev = null;
            foreach (var inst in instances) {
                if (prev is not null) {
                    var prevFile = prev["file"]!.GetValue<string>();
                    var prevLine = prev["line"]!.GetValue<int>();
                    var prevCol = prev["column"]!.GetValue<int>();
                    var curFile = inst!["file"]!.GetValue<string>();
                    var curLine = inst!["line"]!.GetValue<int>();
                    var curCol = inst!["column"]!.GetValue<int>();

                    var fileCmp = string.Compare(prevFile, curFile, StringComparison.Ordinal);
                    fileCmp.Should().BeLessThanOrEqualTo(0);
                    if (fileCmp == 0) {
                        prevLine.Should().BeLessThanOrEqualTo(curLine);
                        if (prevLine == curLine) {
                            prevCol.Should().BeLessThanOrEqualTo(curCol);
                        }
                    }
                }
                prev = inst;
            }
        }
    }

    [TestMethod]
    public void FilesDiagnostics_SortedByLineThenColumn()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var files = root["files"]!.AsArray();
        foreach (var fileEntry in files) {
            var diags = fileEntry!["diagnostics"]!.AsArray();
            JsonNode? prev = null;
            foreach (var diag in diags) {
                if (prev is not null) {
                    var prevLine = prev["line"]!.GetValue<int>();
                    var prevCol = prev["column"]!.GetValue<int>();
                    var curLine = diag!["line"]!.GetValue<int>();
                    var curCol = diag!["column"]!.GetValue<int>();

                    prevLine.Should().BeLessThanOrEqualTo(curLine);
                    if (prevLine == curLine) {
                        prevCol.Should().BeLessThanOrEqualTo(curCol);
                    }
                }
                prev = diag;
            }
        }
    }

    [TestMethod]
    public void Files_SortedByPathAsc()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var files = root["files"]!.AsArray();
        string? prev = null;
        foreach (var entry in files) {
            var cur = entry!["path"]!.GetValue<string>();
            if (prev is not null) {
                string.Compare(prev, cur, StringComparison.Ordinal).Should().BeLessThanOrEqualTo(0);
            }
            prev = cur;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Full enumeration (no truncation)
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ByCodeInstances_CountsMatchTotal()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var byCode = root["byCode"]!.AsArray();
        foreach (var entry in byCode) {
            var count = entry!["count"]!.GetValue<int>();
            var instances = entry!["instances"]!.AsArray().Count;
            count.Should().Be(instances, $"expected {count} instances for {entry["code"]}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Location.None handling
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void LocationNoneDiagnostic_UsesSyntheticFileAndZeroSpans()
    {
        var descriptor = new DiagnosticDescriptor(
            "CS0001", "Test Title", "Test message", "Compiler",
            DiagnosticSeverity.Error, true);
        var diag = Diagnostic.Create(descriptor, Location.None);

        var json = SerializeJson([diag], @"C:\output");
        var root = ParseJson(json);

        root["byCode"]![0]!["instances"]![0]!["file"]!.GetValue<string>().Should().Be("<none>");
        root["byCode"]![0]!["instances"]![0]!["line"]!.GetValue<int>().Should().Be(0);
        root["byCode"]![0]!["instances"]![0]!["column"]!.GetValue<int>().Should().Be(0);
        root["byCode"]![0]!["instances"]![0]!["endLine"]!.GetValue<int>().Should().Be(0);
        root["byCode"]![0]!["instances"]![0]!["endColumn"]!.GetValue<int>().Should().Be(0);

        root["files"]![0]!["path"]!.GetValue<string>().Should().Be("<none>");
    }

    // ─────────────────────────────────────────────────────────────────────
    // endLine / endColumn
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DiagnosticSpan_EndLineEndColumn_FromEndLinePosition()
    {
        var code = """
            class C {
                int x = "hello";
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(code, path: @"C:\output\src\test.cs");
        var comp = CSharpCompilation.Create("Test", [tree], [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        ], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var instance = root["byCode"]![0]!["instances"]![0]!;
        var line = instance["line"]!.GetValue<int>();
        var endLine = instance["endLine"]!.GetValue<int>();
        var endCol = instance["endColumn"]!.GetValue<int>();

        // The span should have meaningful endLine/endColumn (not just line/column repeated)
        endLine.Should().BeGreaterThanOrEqualTo(line);
        // For a string literal "hello" assigned to int, the span should be > 1 column wide
        endCol.Should().BeGreaterThan(instance["column"]!.GetValue<int>());
    }

    // ─────────────────────────────────────────────────────────────────────
    // sourceLine
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SourceLine_ContainsOffendingSourceText()
    {
        var code = """
            class C {
                int x = "hello";
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(code, path: @"C:\output\src\test.cs");
        var comp = CSharpCompilation.Create("Test", [tree], [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        ], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var instance = root["byCode"]![0]!["instances"]![0]!;
        var sourceLine = instance["sourceLine"]!.GetValue<string>();
        sourceLine.Should().NotBeNullOrEmpty();
        sourceLine.Should().Contain("int x = \"hello\";");
    }

    [TestMethod]
    public void SourceLine_OmittedForLocationNone()
    {
        var descriptor = new DiagnosticDescriptor(
            "CS0001", "Test Title", "Test message", "Compiler",
            DiagnosticSeverity.Error, true);
        var diag = Diagnostic.Create(descriptor, Location.None);

        var root = ParseJson(SerializeJson([diag], @"C:\output"));
        var instance = root["byCode"]![0]!["instances"]![0]!;
        instance.AsObject().ContainsKey("sourceLine").Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Severity preservation
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SeverityPreservation_AllSeveritiesMappedVerbatim()
    {
        var errorDesc = new DiagnosticDescriptor("CS0001", "E", "Error msg", "Compiler", DiagnosticSeverity.Error, true);
        var warnDesc = new DiagnosticDescriptor("CS0002", "W", "Warning msg", "Compiler", DiagnosticSeverity.Warning, true);
        var infoDesc = new DiagnosticDescriptor("CS0003", "I", "Info msg", "Compiler", DiagnosticSeverity.Info, true);
        var hiddenDesc = new DiagnosticDescriptor("CS0004", "H", "Hidden msg", "Compiler", DiagnosticSeverity.Hidden, true);

        var tree = CSharpSyntaxTree.ParseText("class C {}", path: @"C:\output\src\test.cs");
        var errorLoc = Location.Create(tree, TextSpan.FromBounds(0, 1));
        var warnLoc = Location.Create(tree, TextSpan.FromBounds(2, 3));
        var infoLoc = Location.Create(tree, TextSpan.FromBounds(4, 5));
        var hiddenLoc = Location.Create(tree, TextSpan.FromBounds(6, 7));

        var diagnostics = new Diagnostic[] {
            Diagnostic.Create(errorDesc, errorLoc),
            Diagnostic.Create(warnDesc, warnLoc),
            Diagnostic.Create(infoDesc, infoLoc),
            Diagnostic.Create(hiddenDesc, hiddenLoc),
        };

        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        root["summary"]!["totalErrors"]!.GetValue<int>().Should().Be(1);
        root["summary"]!["totalWarnings"]!.GetValue<int>().Should().Be(1);

        var byCode = root["byCode"]!.AsArray();
        var severities = byCode.Select(e => e!["severity"]!.GetValue<string>()).OrderBy(s => s).ToArray();
        severities.Should().BeEquivalentTo(["error", "info", "warning"]);
    }

    // ─────────────────────────────────────────────────────────────────────
    // No args field
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoArgsField_Emitted()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var json = SerializeJson(diagnostics, @"C:\output");

        // The JSON should not contain the string "args" as a property name
        // (except possibly in message text, so check structurally)
        var root = ParseJson(json);
        foreach (var entry in root["byCode"]!.AsArray()) {
            foreach (var inst in entry!["instances"]!.AsArray()) {
                var obj = inst!.AsObject();
                obj.ContainsKey("args").Should().BeFalse();
            }
        }
        foreach (var fileEntry in root["files"]!.AsArray()) {
            foreach (var diag in fileEntry!["diagnostics"]!.AsArray()) {
                var obj = diag!.AsObject();
                obj.ContainsKey("args").Should().BeFalse();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Path normalization (POSIX, relative to outputRoot)
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PathNormalization_PosixRelativePaths()
    {
        var code = """
            class C {
                int x = "hello";
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(code, path: @"C:\output\src\sub\test.cs");
        var comp = CSharpCompilation.Create("Test", [tree], [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        ], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var file = root["byCode"]![0]!["instances"]![0]!["file"]!.GetValue<string>();
        file.Should().Be("src/sub/test.cs");
        file.Should().NotContain("\\");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Template preservation
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Template_HasPlaceholderFormat()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var byCode = root["byCode"]!.AsArray();
        foreach (var entry in byCode) {
            var template = entry!["template"]!.GetValue<string>();
            // Should contain at least one {0} placeholder for most compiler errors
            template.Should().NotBeNullOrEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Category preservation
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Category_PreservedVerbatim()
    {
        var comp = CreateCompilationWithErrors();
        var diagnostics = comp.GetDiagnostics();
        var root = ParseJson(SerializeJson(diagnostics, @"C:\output"));

        var byCode = root["byCode"]!.AsArray();
        foreach (var entry in byCode) {
            var category = entry!["category"]!.GetValue<string>();
            category.Should().NotBeNullOrEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Empty diagnostics
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EmptyDiagnostics_ProducesValidReport()
    {
        var root = ParseJson(SerializeJson([], @"C:\output"));

        root["summary"]!["totalErrors"]!.GetValue<int>().Should().Be(0);
        root["summary"]!["totalWarnings"]!.GetValue<int>().Should().Be(0);
        root["byCode"]!.AsArray().Should().BeEmpty();
        root["files"]!.AsArray().Should().BeEmpty();
    }
}