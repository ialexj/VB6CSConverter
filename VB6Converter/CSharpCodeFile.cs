using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;

namespace VB6Converter;

public record class CSharpCodeFile(string Name, CompilationUnitSyntax CompilationUnit)
{
    public void Write(string path)
    {
        File.WriteAllText(path, CompilationUnit.NormalizeWhitespace().ToFullString());
    }
}
