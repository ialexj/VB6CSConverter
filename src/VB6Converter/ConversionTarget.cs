using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VB6Parser;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter;

[DebuggerDisplay("{Name}")]
public class ConversionTarget(VisualBasicProjectFile file, string outputPath)
{
    public VisualBasicProjectFile File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    public string Name => File.Name;

    public string OutputDocumentName => File.Name + ".cs";

    public string OutputPath { get; } = outputPath ?? throw new ArgumentNullException(nameof(outputPath));

    public bool Exists => System.IO.File.Exists(OutputPath);

    public bool HasErrors => System.IO.File.Exists($"{OutputPath}.log");

    public string DesignerOutputPath => Path.Combine(
        Path.GetDirectoryName(OutputPath)!,
        Path.GetFileNameWithoutExtension(OutputPath) + ".designer.cs");

    public static ConversionTarget CreateForSplit(string name, string outputPath)
        => new ConversionTarget(new VisualBasicProjectFile(outputPath, name, VisualBasicFileType.Module), outputPath);

    public static ConversionTarget Create(VisualBasicProjectFile file, string outDir, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, file.Path);

        // If the file is outside the root (e.g. --root was specified too narrowly),
        // fall back to placing it by filename at the output root.
        var isOutsideRoot = relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

        if (isOutsideRoot)
            return new ConversionTarget(file, Path.Combine(outDir, $"{file.Name}.cs"));

        var outputRelativePath = Path.ChangeExtension(relativePath, ".cs");
        return new ConversionTarget(file, Path.Combine(outDir, outputRelativePath));
    }

    public static IEnumerable<ConversionTarget> CreateAll(VisualBasicProjectFile file, string outDir, string rootPath)
    {
        yield return Create(file, outDir, rootPath);

        var relativePath = Path.GetRelativePath(rootPath, file.Path);
        var outputRelativePath = Path.ChangeExtension(relativePath, ".designer.cs");
        if (System.IO.File.Exists(Path.Combine(outDir, outputRelativePath))) {
            yield return new ConversionTarget(file, Path.Combine(outDir, outputRelativePath));
        }
    }
}

