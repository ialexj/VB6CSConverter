using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace VB6Converter.Rewriters;
public class LoggedRewriter() : CSharpSyntaxRewriter
{
    readonly string _file;
    public LoggedRewriter(string file) : this()
    {
        _file = file;
    }

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
        var log = Log.Rewriting
            .ForContext("file", _file ?? Path.GetFileNameWithoutExtension(node.SyntaxTree.FilePath))
            .ForContext("rewriter", GetType().Name)
            .ForContext("node", node);

        try {
            var @new = change(node);

            if (log.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
                var oldValue = value?.Invoke(node) ?? node;
                var newValue = value?.Invoke(@new) ?? @new;

                if (!RoslynHelpers.IsEquivalentSyntax(oldValue, newValue)) {
                    log.ForContext("node", oldValue).ForContext("changed", newValue).Verbose("{node} --> {changed}");
                }
            }

            return @new;
        }
        catch (Exception ex) when (!Debugger.IsAttached) {
            log.ForContext("error", ex.Message)
                .ForContext("method", ex.TargetSite.Name)
                .Error("Failed to rewrite {node} ({method}): {error:nq}");

            throw;
        }
    }
}
