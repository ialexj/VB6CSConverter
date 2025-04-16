using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using VB6Parser;
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
        return node.WithLeadingTrivia(Comment($"// ERROR: {error.Message} @ {error.Line}:{error.Col}{Environment.NewLine}"))
            .WithAdditionalAnnotations(GetErrorAnnotations(error));
    }

    public static SyntaxToken WithError(this SyntaxToken token, TransformError error)
    {
        return token.WithLeadingTrivia(Comment($"/* ERROR: {error.Message} */"))
            .WithAdditionalAnnotations(GetErrorAnnotations(error));
    }
}

public record class TransformError(string Message, string Source, string ErrorTree, int Line, int Col)
{
    public static TransformError NotSupported(IParseTree ctx, string message = null, [CallerMemberName] string caller = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        message ??= "Not supported";
        message += $" from {caller}({ctx.GetType().Name})";

        var errorNode = ctx.GetText();
        var errorTree = new ConsoleVisitor().Visit(ctx);

        int errorLine = 0, errorCol = 0;
        if (ctx is ParserRuleContext syntax) {
            errorLine = syntax.Start.Line;
            errorCol = syntax.Start.Column;
        }
        
        return new TransformError(message, errorNode, errorTree, errorLine, errorCol);
    }
};
