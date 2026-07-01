using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace VB6Converter.Rewriters;
public class LoggedRewriter() : CSharpSyntaxRewriter
{
    readonly string _file;
    public LoggedRewriter(string file) : this()
    {
        _file = file;
    }

    /// <summary>
    /// The sequence number for this rewriter run.
    /// Set when the rewriter is instantiated to provide unique identification across iterations.
    /// </summary>
    public long RewriterSequence { get; set; }

    public Action<int, int> Progress { get; set; }

    [return: NotNullIfNotNull(nameof(node))]
    public override SyntaxNode Visit(SyntaxNode node)
    {
        if (node != null && Progress != null) {
            Progress(node.Span.End, node.SyntaxTree.Length);
        }

        return base.Visit(node);
    }

    protected SyntaxNode Rewrite<T>(T node, Func<T, SyntaxNode> change, Func<SyntaxNode, object> value = null) where T : SyntaxNode
    {
        string path = _file ?? node?.SyntaxTree.FilePath;

        var file = Path.GetFileNameWithoutExtension(path);
        var log = Log.Rewriting
            .ForContext("file", file)
            .ForContext("rewriter", GetType().Name)
            .ForContext("sequence", RewriterSequence)
            .ForContext("node", node);

        try {
            var @new = change(node);

            if (log.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                var oldValue = value?.Invoke(node) ?? node;
                if (oldValue is SyntaxNode oldNode) {
                    oldValue = oldNode.NormalizeWhitespace();
                }

                var newValue = value?.Invoke(@new) ?? @new;
                if (newValue is SyntaxNode newNode) {
                    newValue = newNode.NormalizeWhitespace();
                }

                if (!RoslynHelpers.IsEquivalentSyntax(oldValue, newValue)) {
                    log.Verbose("{json:l}", JsonSerializer.Serialize(new {
                        sequence = RewriterSequence,
                        rewriter = GetType().Name,
                        file = file,
                        line = node?.GetLocation()?.GetLineSpan().StartLinePosition.Line,
                        from = oldValue?.ToString(),
                        to   = newValue?.ToString()
                    }));
                }
            }

            return @new;
        }
        catch (Exception ex) when (!Debugger.IsAttached) {
            var location = node?.GetLocation()?.GetLineSpan();
            log.ForContext("error", ex.Message)
                .ForContext("method", ex.TargetSite?.Name)
                .ForContext("nodeType", node?.GetType().Name)
                .ForContext("filePath", location?.Path)
                .ForContext("line", location.HasValue ? location.Value.StartLinePosition.Line + 1 : (int?)null)
                .Error("Failed to rewrite {nodeType} at {filePath}:{line} ({method}): {error:nq}");

            throw;
        }
    }
}
