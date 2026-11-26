#nullable enable
using System.Text;

namespace VB6Instrument;

/// <summary>
/// A VB6 source file loaded as raw text with byte-exact encoding and line
/// ending discipline: a UTF-8 BOM means UTF-8, anything else is the
/// configured code page (default CP1252); line endings are preserved
/// per-line (CRLF expected, a missing final newline survives, lone CR is
/// rejected); the file is rewritten only when its bytes actually change.
/// </summary>
public sealed class SourceFile
{
    private readonly string _originalText;

    private SourceFile(string path, string originalText, string[] contents, string[] terminators, bool hasBom, int codePage)
    {
        Path = path;
        _originalText = originalText;
        Contents = contents;
        Terminators = terminators;
        HasBom = hasBom;
        CodePage = codePage;
    }

    public string Path { get; }

    /// <summary>Mutable working copy of the line contents (no terminators).</summary>
    public string[] Contents { get; set; }

    /// <summary>Per-line terminators, parallel to <see cref="Contents"/>. The last entry may be empty.</summary>
    public string[] Terminators { get; set; }

    public bool HasBom { get; }
    public int CodePage { get; }

    public int LineCount => Contents.Length;

    public static SourceFile Load(string path, int codePage)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var readEncoding = hasBom ? Encoding.UTF8 : Encoding.GetEncoding(codePage);
        var text = System.IO.File.ReadAllText(path, readEncoding);

        if (text.Contains('\r') && !text.Contains("\r\n")) {
            throw new InstrumentationException(path, "Lone CR line endings are not supported.");
        }

        var (contents, terminators) = SplitLines(text);
        return new SourceFile(path, text, contents, terminators, hasBom, codePage);
    }

    /// <summary>
    /// Splits VB6 source into contents + terminators, preserving CRLF/LF
    /// endings and a missing final newline byte-exactly.
    /// </summary>
    public static (string[] Contents, string[] Terminators) SplitLines(string text)
    {
        var segments = System.Text.RegularExpressions.Regex.Split(text, @"(?<=\r\n)|(?<=\n)");
        var contents = new string[segments.Length];
        var terminators = new string[segments.Length];
        for (var index = 0; index < segments.Length; index++) {
            var segment = segments[index];
            if (segment.EndsWith("\r\n")) {
                contents[index] = segment[..^2];
                terminators[index] = "\r\n";
            }
            else if (segment.EndsWith('\n')) {
                contents[index] = segment[..^1];
                terminators[index] = "\n";
            }
            else {
                contents[index] = segment;
                terminators[index] = string.Empty;
            }
        }
        return (contents, terminators);
    }

    public string BuildText()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < Contents.Length; index++) {
            builder.Append(Contents[index]);
            builder.Append(Terminators[index]);
        }
        return builder.ToString();
    }

    public bool IsChanged => BuildText() != _originalText;

    /// <summary>
    /// Writes the file with its original encoding (BOM state preserved) —
    /// only when the bytes actually change. A BOM-less file is never
    /// re-encoded as UTF-8.
    /// </summary>
    public void WriteIfChanged()
    {
        if (!IsChanged) {
            return;
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var writeEncoding = HasBom ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : Encoding.GetEncoding(CodePage);
        // WriteAllText emits the BOM preamble for the UTF-8 encoder;
        // GetBytes/WriteAllBytes would silently drop it.
        System.IO.File.WriteAllText(Path, BuildText(), writeEncoding);
    }
}
