using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using VB6Parser;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter;

public class ConversionTarget(VisualBasicProjectFile file, string outputPath)
{
    public VisualBasicProjectFile File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    public string OutputPath { get; } = outputPath ?? throw new ArgumentNullException(nameof(outputPath));

    public bool Exists => System.IO.File.Exists(OutputPath);

    public bool HasErrors => System.IO.File.Exists($"{OutputPath}.log");

    public CompilationUnitSyntax CompilationUnit { get; set; }


    public static ConversionTarget Create(VisualBasicProjectFile file, string outDir)
    {
        return new ConversionTarget(file, Path.Combine(outDir, $"{file.Name}.cs"));
    }
}
  
