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
        writer.WriteLine();
        writer.WriteLine("Global Diagnostics:");

        WriteStatistics(writer, diagnostics, "");

        writer.WriteLine();
        writer.WriteLine("=======================================================");
        writer.WriteLine("Files:");

        foreach (var file in diagnostics.Where(d => d.Location != null).GroupBy(d => d.Location.SourceTree.FilePath).OrderByDescending(f => f.Count())) {
            writer.WriteLine($"--- {Path.GetFileNameWithoutExtension(file.Key)} ---");
            WriteStatistics(writer, file, "   ");

            writer.WriteLine();

            var messages = file.GroupBy(gg => gg.GetMessage())
                .Select(g => (message: g.Key, count: g.Count()))
                .OrderByDescending(g => g.count);

            foreach (var id in messages) {
                writer.WriteLine($"      {id.count,-6} - {id.message}");
            }
        }
    }

    static void WriteStatistics(TextWriter writer, IEnumerable<Diagnostic> diagnostics, string prefix)
    {
        foreach (var severity in diagnostics.GroupBy(d => d.Severity)) {
            writer.WriteLine($"{prefix}{severity.Key}: {severity.Count()}");

            var ids = severity.GroupBy(gg => new { gg.Id, gg.Descriptor.MessageFormat })
                .Select(g => (id: g.Key, count: g.Count()))
                .OrderByDescending(g => g.count);

            foreach (var id in ids) {
                writer.WriteLine($"{prefix}{id.count,-6} - {id.id.Id} {id.id.MessageFormat}");
            }
        }

        writer.WriteLine();
    }
}
