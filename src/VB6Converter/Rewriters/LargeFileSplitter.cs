using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.Rewriters;

/// <summary>
/// Splits a large CompilationUnit into multiple numbered partial-class CUs at member boundaries.
/// </summary>
public static class LargeFileSplitter
{
    /// <summary>
    /// Splits <paramref name="cu"/> into at most <paramref name="maxLines"/>-line chunks.
    /// Returns a single-element list containing the original CU when no split is needed.
    /// </summary>
    public static IReadOnlyList<CompilationUnitSyntax> Split(CompilationUnitSyntax cu, int maxLines)
    {
        var ns = cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (ns is null) return [cu];

        var cls = ns.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls is null) return [cu];

        // Fast-path: skip split when total file is within budget
        int totalLines = cu.ToFullString().Count(c => c == '\n');
        if (totalLines <= maxLines) return [cu];

        // Partition members into chunks.
        // A chunk is flushed when adding the next member would exceed maxLines
        // AND the current chunk already contains at least one method-like member.
        var chunks = new List<List<MemberDeclarationSyntax>>();
        var currentChunk = new List<MemberDeclarationSyntax>();
        int currentLines = 0;
        bool currentHasMethod = false;

        foreach (var member in cls.Members) {
            int memberLines = member.ToFullString().Count(c => c == '\n');

            if (currentLines > 0 && currentLines + memberLines > maxLines && currentHasMethod) {
                chunks.Add(currentChunk);
                currentChunk = [];
                currentLines = 0;
                currentHasMethod = false;
            }

            currentChunk.Add(member);
            currentLines += memberLines;
            if (IsMethodLike(member)) currentHasMethod = true;
        }

        if (currentChunk.Count > 0) chunks.Add(currentChunk);

        // If we couldn't split (e.g., a single giant method), return unchanged
        if (chunks.Count <= 1) return [cu];

        // Build one CompilationUnit per chunk
        var result = new List<CompilationUnitSyntax>(chunks.Count);
        for (int i = 0; i < chunks.Count; i++) {
            ClassDeclarationSyntax chunkClass;

            if (i == 0) {
                // First chunk: preserve BaseList and existing attributes
                chunkClass = cls.WithMembers(List(chunks[i]));
            }
            else {
                // Subsequent chunks: fresh partial class declaration
                chunkClass = ClassDeclaration(cls.Identifier)
                    .WithModifiers(cls.Modifiers)
                    .WithMembers(List(chunks[i]));
            }

            var chunkNs = FileScopedNamespaceDeclaration(ns.Name)
                .WithUsings(ns.Usings)
                .WithMembers(SingletonList<MemberDeclarationSyntax>(chunkClass));

            var chunkCu = SyntaxFactory.CompilationUnit()
                .WithUsings(cu.Usings)
                .WithMembers(SingletonList<MemberDeclarationSyntax>(chunkNs));

            result.Add(chunkCu);
        }

        return result;
    }

    static bool IsMethodLike(MemberDeclarationSyntax member)
        => member is MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax;
}
