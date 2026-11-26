#nullable enable
using System.IO;

namespace VB6Instrument;

/// <summary>
/// Per-file working model shared by the instrumentation passes. Passes
/// mutate <see cref="Contents"/> in place (line edits) and collect line
/// insertions applied bottom-up; the model is re-parsed between passes so
/// every pass sees post-previous-pass reality.
/// </summary>
public sealed class Vb6FileModel
{
    public required SourceFile Source { get; init; }
    public required string FilePath { get; init; }

    /// <summary>File name without extension — the {File} placeholder value.</summary>
    public required string FileName { get; init; }

    public required string Extension { get; init; }

    /// <summary>0-based index of the <c>Attribute VB_Name</c> line (code section start), or -1 when the file has none.</summary>
    public required int CodeStartIndex { get; set; }

    public required List<Vb6Statement> Statements { get; set; }

    public required List<Vb6MemberRange> Members { get; set; }

    public string[] Contents
    {
        get => Source.Contents;
        set => Source.Contents = value;
    }

    public string[] Terminators
    {
        get => Source.Terminators;
        set => Source.Terminators = value;
    }

    public int LineCount => Contents.Length;
}

public static class Vb6FileParser
{
    /// <summary>Parses a freshly loaded source file.</summary>
    public static Vb6FileModel Parse(SourceFile source)
    {
        var extension = Path.GetExtension(source.Path).ToLowerInvariant();
        var model = new Vb6FileModel {
            Source = source,
            FilePath = source.Path,
            FileName = Path.GetFileNameWithoutExtension(source.Path),
            Extension = extension,
            CodeStartIndex = Vb6MemberScanner.GetCodeStartIndex(source.Contents, extension),
            Statements = [],
            Members = [],
        };
        Refresh(model);
        return model;
    }

    /// <summary>Re-derives statements and member ranges from the current contents.</summary>
    public static void Refresh(Vb6FileModel model)
    {
        // Start after the Attribute VB_Name line itself (whose value can
        // contain tokenizer-hostile characters); without it, all lines are
        // scanned.
        var firstLine = model.CodeStartIndex >= 0 ? model.CodeStartIndex + 2 : 1;
        model.Statements = Vb6Lexer.Tokenize(model.Contents, firstLine, model.FilePath);
        model.Members = Vb6MemberScanner.ScanMembers(model.FilePath, model.Statements);
    }
}
