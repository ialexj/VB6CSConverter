#nullable enable
using System.Text;

namespace VB6Instrument.Tests;

/// <summary>Shared helpers: run passes over synthetic sources through a real
/// temp file so encoding and write discipline are exercised end to end.</summary>
public static class TestSources
{
    public static string Crlf(string text) =>
        text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    /// <summary>Writes the source (normalized to CRLF) to a temp file, runs
    /// the given passes, and returns the resulting file text. The file is
    /// deleted afterwards.</summary>
    public static string Run(string text, Action<Vb6FileModel>[] passes, string? fileName = null, Encoding? encoding = null, bool bom = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName ?? $"test-{Guid.NewGuid():N}.bas");
        if (File.Exists(path)) {
            File.Delete(path);
        }

        var resolvedEncoding = encoding ?? (bom ? new UTF8Encoding(true) : Encoding.ASCII);
        File.WriteAllText(path, Crlf(text), resolvedEncoding);
        try {
            var source = SourceFile.Load(path, 1252);
            var model = Vb6FileParser.Parse(source);
            foreach (var pass in passes) {
                pass(model);
            }
            return source.BuildText();
        }
        finally {
            File.Delete(path);
        }
    }

    public static string RunLineNumbers(string text, string? fileName = null) =>
        Run(text, [model => new LineNumbersPass().Run(model)], fileName);

    /// <summary>Runs pass A and also returns its result (with the captured
    /// virtual-to-real line map).</summary>
    public static (string Text, LineNumbersResult Result) RunLineNumbersMapped(string text, string? fileName = null)
    {
        LineNumbersResult? result = null;
        var output = Run(text, [model => result = new LineNumbersPass().Run(model)], fileName);
        return (output, result!);
    }

    public static string RunTraces(string text, string template, string? fileName = null) =>
        Run(text, [model => new CallTracePass(template).Run(model)], fileName);

    public static string RunGuards(string text, string? statement = null, ErrorPropagation propagation = ErrorPropagation.Raise, string? fileName = null) =>
        Run(text, [model => new ErrorGuardPass(statement, propagation).Run(model)], fileName);

    public static string RunAll(string text, string traceTemplate, string? guardStatement = null, string? fileName = null) =>
        Run(
            text,
            [
                model => new LineNumbersPass().Run(model),
                model => new CallTracePass(traceTemplate).Run(model),
                model => new ErrorGuardPass(guardStatement, ErrorPropagation.FallThrough).Run(model),
            ],
            fileName);
}
