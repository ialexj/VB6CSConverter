#nullable enable
namespace VB6Instrument;

/// <summary>
/// A fatal instrumentation error (parse anomaly, encoding problem, number
/// overflow). Names the file — and where known the member and line — and
/// aborts the run with a nonzero exit; no partial writes are performed.
/// </summary>
public sealed class InstrumentationException : Exception
{
    public string? FilePath { get; }

    public InstrumentationException(string? filePath, string message)
        : base(string.IsNullOrEmpty(filePath) ? message : $"{filePath}: {message}")
    {
        FilePath = filePath;
    }
}
