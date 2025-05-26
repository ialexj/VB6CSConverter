using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VB6Converter.Conversion;
using VB6Parser;
using static VB6Parser.VisualBasic6Parser;
namespace VB6Converter;

public record class VB6ToCSharpConversion(string Name, CompilationUnitSyntax CompilationUnit)
{
    public static VB6ToCSharpConversion ConvertFile(string input, string output)
    {
        var name = Path.GetFileNameWithoutExtension(input);
        var type = VisualBasicFileTypes.GetFromExtension(input);
        return ConvertFile(input, output, name, name, type);
    }

    public static VB6ToCSharpConversion ConvertFile(string input, string output, string className, string nsName, VisualBasicFileType type)
    {
        using var reader = OpenFile(input);

        var conversion = Convert(reader, className, nsName, type);

        // Write output
        File.WriteAllText(output, conversion.CompilationUnit.ToFullString());

        File.Delete($"{output}.log");
        File.Delete($"{output}.tokens");
        File.Delete($"{output}.syntax.lisp");
        File.Delete($"{output}.vb6");

        if (conversion.ParseErrors.Count > 0) {
            using var writer = new StreamWriter($"{output}.log", true);
            foreach (var error in conversion.ParseErrors) {
                writer.WriteLine($"{error.Line}:{error.Col} {error.Message}");
            }

            File.WriteAllText($"{output}.vb6", conversion.Parse.Source);
        }

        // Create log file with errors
        if (conversion.TransformErrors.Count > 0) {
            using var writer = new StreamWriter($"{output}.log", true);
            foreach (var error in conversion.TransformErrors) {
                writer.WriteLine($"{error.Line}:{error.Col} {error.Message}");
                writer.WriteLine();
                writer.WriteLine(error.Source);
                writer.WriteLine();
                writer.WriteLine(error.ErrorTree);
                writer.WriteLine();
                writer.WriteLine("========================");
            }
        }

        return conversion;
    }

    public static VB6ToCSharpConversion ConvertString(string vb6CodeString, string className, string nsName = null, VisualBasicFileType type = VisualBasicFileType.Module)
    {
        using var reader = new StringReader(vb6CodeString);
        return Convert(reader, className, nsName, type);
    }

    public static VB6ToCSharpConversion Convert(TextReader input, string className, string nsName, VisualBasicFileType type)
    {
        ArgumentNullException.ThrowIfNull(input);

        var log = Log.ForFile(className);

        try {
            var parse = Parse(input, className);
            
            var cu = CompilationUnitConverter.GetCompilationUnit(
                parse.Start.module(), nsName, className,
                isStatic: type == VisualBasicFileType.Module);

            var transformErrors = cu.GetTransformErrors().ToArray();
            foreach (var error in transformErrors) {
                log.Warning("{file} TRANSFORM ERROR: {error}", className, error.Message);
            }

            // Basic compile and check for syntax errors
            var syntaxErrors = GetCompilation([cu]).GetParseDiagnostics();
            foreach (var diag in syntaxErrors) {
                log.Warning("{file} SYNTAX ERROR: {error}", className, diag.ToString());
            }

            return new VB6ToCSharpConversion(className, cu) {
                Parse = parse,
                TransformErrors = transformErrors,
                SyntaxErrors = syntaxErrors
            };
        }
        catch (ParseException pex) {
            foreach (var error in pex.Errors) {
                log.Warning("{file} PARSE ERROR: {error}", className, error);
            }

            return new VB6ToCSharpConversion(className, SyntaxFactory.CompilationUnit()) {
                Parse = pex.Context,
                ParseErrors = pex.Errors
            };
        }
    }

    public static CSharpCompilation GetCompilation(IEnumerable<CompilationUnitSyntax> files)
    {
        var references = new MetadataReference[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        return CSharpCompilation.Create(
            "Project", files.Select(cu => cu.SyntaxTree), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }


    public FileScopedNamespaceDeclarationSyntax Namespace => CompilationUnit.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();

    public ClassDeclarationSyntax Class => Namespace?.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();

    public ParseContext Parse { get; init; }


    public IReadOnlyCollection<ParseError> ParseErrors { get; init; } = [];

    public IReadOnlyCollection<TransformError> TransformErrors { get; init; } = [];

    public IReadOnlyCollection<Diagnostic> SyntaxErrors { get; init; } = [];
}
