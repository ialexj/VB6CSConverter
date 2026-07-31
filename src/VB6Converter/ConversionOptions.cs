namespace VB6Converter;

/// <summary>
/// Options that control the behavior of the VB6 to C# conversion.
/// </summary>
public record ConversionOptions
{
    public static readonly ConversionOptions Default = new();

    /// <summary>
    /// When true (default), untyped, Object, and Variant VB6 declarations are emitted
    /// as <c>dynamic</c> rather than <c>object</c>, matching VB6's late-binding semantics.
    /// Pass <c>--use-object</c> on the command line to disable.
    /// </summary>
    public bool UseDynamic { get; init; } = true;

    /// <summary>
    /// When true (default), the pre-processed VB6 source is written back to the original file.
    /// </summary>
    public bool WritePreprocessedSource { get; init; } = true;
}
