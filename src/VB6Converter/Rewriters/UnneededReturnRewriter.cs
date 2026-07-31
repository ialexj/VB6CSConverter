using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

/// <summary>
/// Removes control-flow statements that are immediately after terminal control flow in the same block.
/// - Removes return after throw/goto
/// - Removes break after throw/goto/return
/// This intentionally does not perform general reachability analysis.
/// </summary>
public class UnneededReturnRewriter(string file) : LoggedRewriter(file)
{
    public static readonly UnneededReturnRewriter Default = new("default");

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        => Rewrite(node, node => {
            if (node.Body is null) {
                return base.VisitMethodDeclaration(node);
            }

            var newBody = (BlockSyntax)Visit(node.Body);
            return newBody != node.Body
                ? node.WithBody(newBody)
                : base.VisitMethodDeclaration(node);
        });

    public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        => Rewrite(node, node => {
            if (node.Body is null) {
                return base.VisitAccessorDeclaration(node);
            }

            var newBody = (BlockSyntax)Visit(node.Body);
            return newBody != node.Body
                ? node.WithBody(newBody)
                : base.VisitAccessorDeclaration(node);
        });

    public override SyntaxNode VisitBlock(BlockSyntax node)
        => Rewrite(node, node => {
            var visited = (BlockSyntax)base.VisitBlock(node);
            var statements = visited.Statements;
            var kept = new List<StatementSyntax>(statements.Count);

            for (int i = 0; i < statements.Count; i++) {
                var current = statements[i];
                var previous = kept.Count > 0 ? kept[^1] : null;

                if (current is ReturnStatementSyntax
                    && (previous is ThrowStatementSyntax || previous is GotoStatementSyntax)) {
                    continue;
                }

                if (current is BreakStatementSyntax
                    && (previous is ThrowStatementSyntax || previous is GotoStatementSyntax || previous is ReturnStatementSyntax)) {
                    continue;
                }

                kept.Add(current);
            }

            return kept.Count == statements.Count
                ? visited
                : visited.WithStatements(List(kept));
        });
}
