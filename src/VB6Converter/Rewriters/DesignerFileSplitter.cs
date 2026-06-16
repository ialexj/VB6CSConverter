using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.Rewriters;

/// <summary>
/// Splits a Form or Control CompilationUnit into a main class (code-behind only)
/// and a partial designer class (control fields + InitializeComponent).
/// </summary>
public static class DesignerFileSplitter
{
    /// <summary>
    /// Splits <paramref name="cu"/> into a main CU (code-behind) and a designer CU.
    /// Returns <c>(cu, null)</c> when no explicit designer section is found.
    /// </summary>
    public static (CompilationUnitSyntax Main, CompilationUnitSyntax Designer) Split(CompilationUnitSyntax cu)
    {
        var ns = cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (ns is null) return (cu, null);

        var cls = ns.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls is null) return (cu, null);

        // Designer section starts at first member with #region in its leading trivia
        int startIdx = -1;
        for (int i = 0; i < cls.Members.Count; i++) {
            if (HasRegionDirective(cls.Members[i])) {
                startIdx = i;
                break;
            }
        }

        // Designer section ends at the InitializeComponent method (always the last designer member)
        int endIdx = -1;
        if (startIdx >= 0) {
            for (int i = cls.Members.Count - 1; i >= startIdx; i--) {
                if (cls.Members[i] is MethodDeclarationSyntax method
                    && method.Identifier.Text == "InitializeComponent") {
                    endIdx = i;
                    break;
                }
            }
        }

        var hasDesignerSection = startIdx >= 0 && endIdx >= startIdx;
        if (!hasDesignerSection) return (cu, null);

        var designerMembers = hasDesignerSection
            ? cls.Members.Skip(startIdx).Take(endIdx - startIdx + 1).ToArray()
            : [];
        var mainMembers = hasDesignerSection
            ? cls.Members.Take(startIdx).Concat(cls.Members.Skip(endIdx + 1)).ToArray()
            : cls.Members.ToArray();

        // Build main class: remove designer members, remove BaseList.
        // After re-parsing, #endregion lives in CloseBraceToken.LeadingTrivia — strip it.
        var mainCloseBrace = cls.CloseBraceToken.WithLeadingTrivia(
            cls.CloseBraceToken.LeadingTrivia
                .Where(t => !t.IsKind(SyntaxKind.EndRegionDirectiveTrivia)));

        var mainClass = cls
            .WithMembers(List(mainMembers))
            .WithBaseList(null)
            .WithCloseBraceToken(mainCloseBrace);

        var mainCu = cu.ReplaceNode(cls, mainClass);

        // Build designer class: strip #region from first member's leading trivia
        var cleanedMembers = designerMembers.ToArray();
        if (cleanedMembers.Length > 0) {
            cleanedMembers[0] = cleanedMembers[0].WithLeadingTrivia(
                cleanedMembers[0].GetLeadingTrivia()
                    .Where(t => !t.IsKind(SyntaxKind.RegionDirectiveTrivia)));
        }

        var designerClass = ClassDeclaration(cls.Identifier)
            .WithModifiers(cls.Modifiers)
            .WithBaseList(cls.BaseList)
            .WithMembers(List(cleanedMembers));

        var designerNs = FileScopedNamespaceDeclaration(ns.Name)
            .WithUsings(ns.Usings)
            .WithMembers(SingletonList<MemberDeclarationSyntax>(designerClass));

        var designerCu = SyntaxFactory.CompilationUnit()
            .WithUsings(cu.Usings)
            .WithMembers(SingletonList<MemberDeclarationSyntax>(designerNs));

        return (mainCu, designerCu);
    }

    static bool HasRegionDirective(MemberDeclarationSyntax member)
        => member.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.RegionDirectiveTrivia));
}
