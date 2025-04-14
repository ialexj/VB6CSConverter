using Microsoft.CodeAnalysis.CSharp.Syntax;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter;

public record struct ConversionTarget(ModuleContext Module, string Name, bool Static) { }

public record struct CompilationTarget(CompilationUnitSyntax CompilationUnit, string Name);