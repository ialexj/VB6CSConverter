using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Conversion;

public static class TransformErrors
{
    public static IEnumerable<SyntaxAnnotation> GetErrorAnnotations(TransformError err)
    {
        yield return new("Error", err.Message);
        yield return new("ErrorSource", err.Source);
        yield return new("ErrorTree", err.ErrorTree);
        yield return new("ErrorLine", err.Line.ToString());
        yield return new("ErrorCol", err.Col.ToString());
    }

    public static IEnumerable<TransformError> GetTransformErrors(this SyntaxNode syntax)
    {
        var errors = syntax.GetAnnotatedNodesAndTokens("Error");

        foreach (var node in errors) {
            var message = node.GetAnnotations("Error").First().Data;
            var nodeText = node.GetAnnotations("ErrorSource").First().Data;
            var errorTree = node.GetAnnotations("ErrorTree").First().Data;
            var errorLine = node.GetAnnotations("ErrorLine").First().Data;
            var errorCol = node.GetAnnotations("ErrorCol").First().Data;

            yield return new TransformError(message, nodeText, errorTree, int.Parse(errorLine), int.Parse(errorCol));
        }
    }

    public static T WithError<T>(this T node, TransformError error) where T : SyntaxNode
    {
        var trivia = node.GetLeadingTrivia();
        trivia = trivia.Insert(0, Comment($"// ERROR: {error.Message} @ {error.Line}:{error.Col}{Environment.NewLine}"));
        return node.WithLeadingTrivia(trivia)
            .WithAdditionalAnnotations(GetErrorAnnotations(error));
    }

    public static T WithError<T>(this T node, TransformError error, string originalContent) where T : SyntaxNode
    {
        return node.WithLeadingTrivia(
            Comment($"// ERROR: {error.Message} @ {error.Line}:{error.Col}{Environment.NewLine}"),
            Comment($"// {originalContent.ReplaceLineEndings($"{Environment.NewLine} //")}"))
            .WithAdditionalAnnotations(GetErrorAnnotations(error));
    }

    public static SyntaxToken WithError(this SyntaxToken token, TransformError error)
    {
        return token.WithLeadingTrivia(Comment($"/* ERROR: {error.Message} */"))
            .WithAdditionalAnnotations(GetErrorAnnotations(error));
    }

    // ── FRX resource annotations ──────────────────────────────────────────────

    private const string FrxResourceAnnotationKind = "FrxResource";

    public static T WithFrxResource<T>(this T node, string resourcePath) where T : SyntaxNode
        => node.WithAdditionalAnnotations(new SyntaxAnnotation(FrxResourceAnnotationKind, resourcePath));

    public static bool HasFrxResource(this SyntaxNode node)
        => node.HasAnnotations(FrxResourceAnnotationKind);

    public static string GetFrxResource(this SyntaxNode node)
        => node.GetAnnotations(FrxResourceAnnotationKind).FirstOrDefault()?.Data;

    // ── Set-assignment marker ──────────────────────────────────────────────
    // Marks a statement as originating from VB6's `Set` keyword (as opposed to
    // `Let`/implicit assignment), so later passes (e.g. default-member expansion)
    // can distinguish object-reference assignment from value assignment. A real
    // comment is used rather than a SyntaxAnnotation because the marker must
    // survive being written to disk and reloaded for the semantic rewriter
    // passes; annotations don't round-trip through source text. NormalizeWhitespace
    // (run after every rewrite pass) forces a line break after any comment trivia
    // regardless of where it's attached, so the marker always ends up on its own
    // line immediately above the statement rather than inline.

    private const string SetAssignmentMarker = "// Set";

    public static T WithSetAssignmentMarker<T>(this T node) where T : SyntaxNode
        => node.WithLeadingTrivia(Comment(SetAssignmentMarker));

    public static bool IsSetAssignment(this SyntaxNode node)
        => node.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) && t.ToString().TrimEnd() == SetAssignmentMarker);
}

public record class TransformError(string Message, string Source, string ErrorTree, int Line, int Col)
{
    public static TransformError Create(IParseTree ctx, string message = null, [CallerMemberName] string caller = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        message ??= "Not supported";
        message += $" from {caller}({ctx.GetType().Name})";

        var errorNode = ctx.GetText();
        var errorTree = new TreeVisitor().Visit(ctx);

        int errorLine = 0, errorCol = 0;
        if (ctx is ParserRuleContext syntax) {
            errorLine = syntax.Start.Line;
            errorCol = syntax.Start.Column;
        }

        return new TransformError(message, errorNode, errorTree, errorLine, errorCol);
    }
};
