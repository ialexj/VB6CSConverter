using System.Threading;

namespace VB6Converter.Rewriters;

/// <summary>
/// Manages a global sequence counter for rewriter runs.
/// Increments on each rewriter pass to provide unique identifiers in log files.
/// </summary>
internal static class RewriterSequenceContext
{
    private static long _sequenceCounter = 0;

    /// <summary>
    /// Gets the current sequence number for this rewriter run.
    /// </summary>
    public static long CurrentSequence => Interlocked.Read(ref _sequenceCounter);

    /// <summary>
    /// Increments and returns the next sequence number for a new rewriter run.
    /// </summary>
    public static long GetNextSequence()
    {
        return Interlocked.Increment(ref _sequenceCounter);
    }

    /// <summary>
    /// Resets the sequence counter (typically called at the start of conversion).
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _sequenceCounter, 0);
    }
}
