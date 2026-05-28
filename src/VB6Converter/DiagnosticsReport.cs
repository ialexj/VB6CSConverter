using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VB6Converter;
public static class DiagnosticsReport
{
    public static void Write(TextWriter writer, IReadOnlyCollection<Diagnostic> diagnostics)
    {
        diagnostics = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        writer.WriteLine();
        writer.WriteLine("Global Diagnostics:");

        WriteStatistics(writer, diagnostics, false);

        writer.WriteLine();
        writer.WriteLine("=======================================================");
        writer.WriteLine("Files:");

        foreach (var file in diagnostics.Where(d => d.Location?.SourceTree != null).GroupBy(d => d.Location.SourceTree.FilePath).OrderByDescending(f => f.Count())) {
            writer.WriteLine($"--- {Path.GetFileNameWithoutExtension(file.Key)} ---");
            WriteStatistics(writer, file, true);

            writer.WriteLine();

            var messages = file.GroupBy(gg => gg.GetMessage())
                .Select(g => (message: g.Key, count: g.Count()))
                .OrderByDescending(g => g.count);

            foreach (var id in messages) {
                writer.WriteLine($"      {id.count,-6} - {id.message}");
            }
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
                writer.WriteLine($"{id.count,-6} - {id.id.Id} {id.id.MessageFormat}");

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
}
