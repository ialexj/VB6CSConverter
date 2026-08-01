using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VB6Converter;
public static class DiagnosticsReport
{
    public static void Write(TextWriter writer, IReadOnlyCollection<Diagnostic> diagnostics, string? outputRoot = null)
    {
        writer.WriteLine($"Diagnostics Report - {DateTime.Now}");
        writer.WriteLine("Global");
        writer.WriteLine("=======================================================");

        diagnostics = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        WriteStatistics(writer, diagnostics, false);

        writer.WriteLine();

        foreach (var file in diagnostics.Where(d => d.Location?.SourceTree != null).GroupBy(d => d.Location.SourceTree.FilePath).OrderByDescending(f => f.Count())) {
            writer.WriteLine($"{GetRelativePath(file.Key, outputRoot)}");
            writer.WriteLine("=======================================================");
            WriteStatistics(writer, file, true);
        }
    }

    internal static string GetRelativePath(string filePath, string? outputRoot)
    {
        if (string.IsNullOrEmpty(outputRoot)) {
            return Path.GetFileName(filePath);
        }

        try {
            var fullPath = Path.GetFullPath(filePath);
            var fullRoot = Path.GetFullPath(outputRoot);
            var relative = Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
            var dir = Path.GetDirectoryName(relative)?.Replace('\\', '/');
            return string.IsNullOrEmpty(dir) || dir == "."
                ? Path.GetFileName(relative)
                : $"{dir}/{Path.GetFileName(relative)}";
        } catch {
            return Path.GetFileName(filePath);
        }
    }

    static void WriteStatistics(TextWriter writer, IEnumerable<Diagnostic> diagnostics, bool detail)
    {
        foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
            writer.WriteLine($"{severity.Key}: {severity.Count()}");

            var ids = severity.GroupBy(gg => new { gg.Id, gg.Descriptor.MessageFormat })
                .Select(g => (id: g.Key, count: g.Count(), g))
                .OrderByDescending(g => g.count);

            foreach (var id in ids) {
                writer.WriteLine($"[{id.count,-6} ] {id.id.Id} {id.id.MessageFormat}");

                if (detail) {
                    foreach (var diag in id.g) {
                        var location = diag.Location;
                        if (location == null || location.IsInMetadata) {
                            continue;
                        }

                        var lineSpan = location.GetLineSpan();
                        var linePosition = lineSpan.StartLinePosition;
                        writer.WriteLine($"      {linePosition.Line + 1},{linePosition.Character + 1} - {diag.GetMessage()}");
                    }
                }
            }
        }

        writer.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────────────
    // JSON diagnostics report — machine-readable, optimized for AI agents
    // ─────────────────────────────────────────────────────────────────────

    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes a deterministic JSON diagnostics report to <paramref name="writer"/>.
    /// The report includes both a cross-file by-code index and a per-file view,
    /// with all instances fully enumerated (no truncation).
    /// </summary>
    public static void WriteJson(TextWriter writer, IReadOnlyCollection<Diagnostic> diagnostics, string? outputRoot)
    {
        var generatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        // ── Single-pass accumulation ──────────────────────────────────────
        var byCode = new Dictionary<(string code, string severity), ByCodeEntry>();
        var byFile = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        int totalErrors = 0, totalWarnings = 0, dropped = 0;

        foreach (var diag in diagnostics) {
            try {
                var severity = MapSeverity(diag.Severity);
                if (severity == "hidden") continue;

                if (diag.Severity == DiagnosticSeverity.Error) totalErrors++;
                else if (diag.Severity == DiagnosticSeverity.Warning) totalWarnings++;

                var file = GetDiagnosticFile(diag, outputRoot);
                var (line, col, endLine, endCol) = GetSpan(diag);

                var instance = new DiagnosticInstance {
                    File = file,
                    Line = line,
                    Column = col,
                    EndLine = endLine,
                    EndColumn = endCol,
                    Message = diag.GetMessage(),
                    SourceLine = GetSourceLine(diag),
                };

                // byCode accumulation
                var key = (diag.Id, severity);
                if (!byCode.TryGetValue(key, out var codeEntry)) {
                    codeEntry = new ByCodeEntry {
                        Code = diag.Id,
                        Severity = severity,
                        Category = diag.Descriptor.Category,
                        Template = diag.Descriptor.MessageFormat?.ToString() ?? "",
                        Count = 0,
                        Instances = [],
                    };
                    byCode[key] = codeEntry;
                }
                codeEntry.Count++;
                codeEntry.Instances.Add(instance);

                // byFile accumulation
                if (!byFile.TryGetValue(file, out var fileEntry)) {
                    fileEntry = new FileEntry {
                        Path = file,
                        ErrorCount = 0,
                        WarningCount = 0,
                        Diagnostics = [],
                    };
                    byFile[file] = fileEntry;
                }
                if (diag.Severity == DiagnosticSeverity.Error) fileEntry.ErrorCount++;
                else if (diag.Severity == DiagnosticSeverity.Warning) fileEntry.WarningCount++;
                fileEntry.Diagnostics.Add(instance);
            }
            catch {
                dropped++;
            }
        }

        // ── Sort ──────────────────────────────────────────────────────────
        // byCode: sort instances by file → line → column
        foreach (var entry in byCode.Values) {
            entry.Instances = [.. entry.Instances.OrderBy(i => i.File)
                .ThenBy(i => i.Line)
                .ThenBy(i => i.Column)];
        }

        // byCode entries: sort by count desc → code asc
        var sortedByCode = byCode.Values
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .ToArray();

        // byFile: sort diagnostics by line → column
        foreach (var entry in byFile.Values) {
            entry.Diagnostics = [.. entry.Diagnostics.OrderBy(d => d.Line)
                .ThenBy(d => d.Column)];
        }

        // byFile entries: sort by errorCount desc → path asc
        var sortedByFile = byFile.Values
            .OrderByDescending(e => e.ErrorCount)
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .ToArray();

        // summary.byCode: sorted by count desc → code asc
        var summaryByCode = sortedByCode
            .Select(e => new SummaryByCodeEntry {
                Code = e.Code,
                Severity = e.Severity,
                Count = e.Count,
                Template = e.Template,
            })
            .ToArray();

        // summary.byFile: sorted by errorCount desc → file asc
        var summaryByFile = sortedByFile
            .Select(e => new SummaryByFileEntry {
                File = e.Path,
                ErrorCount = e.ErrorCount,
                WarningCount = e.WarningCount,
            })
            .ToArray();

        // ── Build root object ─────────────────────────────────────────────
        var report = new DiagnosticsReportRoot {
            GeneratedAt = generatedAt,
            Summary = new Summary {
                TotalErrors = totalErrors,
                TotalWarnings = totalWarnings,
                ByCode = summaryByCode,
                ByFile = summaryByFile,
                DroppedDiagnostics = dropped > 0 ? dropped : null,
            },
            ByCode = sortedByCode,
            Files = sortedByFile,
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        writer.Write(json);
        writer.WriteLine();
    }

    static string MapSeverity(DiagnosticSeverity severity) => severity switch {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "info",
        DiagnosticSeverity.Hidden => "hidden",
        _ => "hidden",
    };

    static string GetDiagnosticFile(Diagnostic diag, string? outputRoot)
    {
        var filePath = diag.Location?.SourceTree?.FilePath;
        if (string.IsNullOrEmpty(filePath))
            return "<none>";
        return GetRelativePath(filePath, outputRoot);
    }

    static (int line, int col, int endLine, int endCol) GetSpan(Diagnostic diag)
    {
        try {
            if (diag.Location == null || diag.Location == Location.None || diag.Location.IsInMetadata)
                return (0, 0, 0, 0);

            var span = diag.Location.GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;
            return (start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1);
        }
        catch {
            return (0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Returns the source text of the line containing the diagnostic's start
    /// position, or null if the diagnostic has no source tree (e.g. Location.None).
    /// </summary>
    static string? GetSourceLine(Diagnostic diag)
    {
        try {
            var tree = diag.Location?.SourceTree;
            if (tree is null) return null;

            var span = diag.Location.GetLineSpan();
            var lineIndex = span.StartLinePosition.Line;
            if (lineIndex < 0) return null;

            var sourceText = tree.GetText();
            if (lineIndex >= sourceText.Lines.Count) return null;

            return sourceText.Lines[lineIndex].ToString();
        }
        catch {
            return null;
        }
    }

    // ── JSON model types ─────────────────────────────────────────────────

    sealed class DiagnosticsReportRoot
    {
        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; set; } = "";
        [JsonPropertyName("summary")]
        public Summary Summary { get; set; } = new();
        [JsonPropertyName("byCode")]
        public ByCodeEntry[] ByCode { get; set; } = [];
        [JsonPropertyName("files")]
        public FileEntry[] Files { get; set; } = [];
    }

    sealed class Summary
    {
        [JsonPropertyName("totalErrors")]
        public int TotalErrors { get; set; }
        [JsonPropertyName("totalWarnings")]
        public int TotalWarnings { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("droppedDiagnostics")]
        public int? DroppedDiagnostics { get; set; }
        [JsonPropertyName("byCode")]
        public SummaryByCodeEntry[] ByCode { get; set; } = [];
        [JsonPropertyName("byFile")]
        public SummaryByFileEntry[] ByFile { get; set; } = [];
    }

    sealed class SummaryByCodeEntry
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";
        [JsonPropertyName("count")]
        public int Count { get; set; }
        [JsonPropertyName("template")]
        public string Template { get; set; } = "";
    }

    sealed class SummaryByFileEntry
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";
        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }
        [JsonPropertyName("warningCount")]
        public int WarningCount { get; set; }
    }

    sealed class ByCodeEntry
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";
        [JsonPropertyName("category")]
        public string Category { get; set; } = "";
        [JsonPropertyName("template")]
        public string Template { get; set; } = "";
        [JsonPropertyName("count")]
        public int Count { get; set; }
        [JsonPropertyName("instances")]
        public List<DiagnosticInstance> Instances { get; set; } = [];
    }

    sealed class FileEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";
        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }
        [JsonPropertyName("warningCount")]
        public int WarningCount { get; set; }
        [JsonPropertyName("diagnostics")]
        public List<DiagnosticInstance> Diagnostics { get; set; } = [];
    }

    sealed class DiagnosticInstance
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";
        [JsonPropertyName("line")]
        public int Line { get; set; }
        [JsonPropertyName("column")]
        public int Column { get; set; }
        [JsonPropertyName("endLine")]
        public int EndLine { get; set; }
        [JsonPropertyName("endColumn")]
        public int EndColumn { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("sourceLine")]
        public string? SourceLine { get; set; }
    }
}
