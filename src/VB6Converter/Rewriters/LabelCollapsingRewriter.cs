using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

/// <summary>
/// Collapses simple goto patterns where the target label is terminal (empty or return-only).
/// Runs before TryCatchRewriter so that plain-end gotos are simplified away,
/// allowing TryCatchRewriter to work on cleaner blocks (it bails on any remaining gotos).
/// </summary>
public class LabelCollapsingRewriter : LoggedRewriter
{
    public static readonly LabelCollapsingRewriter Default = new();

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        => Rewrite(node, node => {
            if (node.Body is null) {
                return base.VisitMethodDeclaration(node);
            }

            var newBody = CollapseGotos(node.Body);
            return newBody != node.Body
                ? node.WithBody(newBody)
                : base.VisitMethodDeclaration(node);
        });

    public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        => Rewrite(node, node => {
            if (node.Body is null) {
                return base.VisitAccessorDeclaration(node);
            }

            var newBody = CollapseGotos(node.Body);
            return newBody != node.Body
                ? node.WithBody(newBody)
                : base.VisitAccessorDeclaration(node);
        });

    /// <summary>
    /// Scans the block for collapsible labeled statements and replaces matching gotos with returns.
    /// A label is collapsible if:
    /// - It has no following statements (or only empty statements) → collapse to `return;`
    /// - It is followed only by a single return statement → collapse to that return
    /// Handles nested labeled statements (e.g., Label1: Label2: Stmt;) by unwrapping to the core statement.
    /// </summary>
    private static BlockSyntax CollapseGotos(BlockSyntax body)
    {
        var map = new Dictionary<string, ReturnStatementSyntax>(StringComparer.OrdinalIgnoreCase);

        // Scan top-level labeled statements in the block
        for (int i = 0; i < body.Statements.Count; i++) {
            if (body.Statements[i] is not LabeledStatementSyntax labeled) {
                continue;
            }

            // Collect all statements starting from the core statement (unwrapping nested labels)
            var coreStmt = UnwrapNestedLabels(labeled.Statement);
            var statements = new List<StatementSyntax> { coreStmt };

            // Add all statements after this label in the block
            for (int j = i + 1; j < body.Statements.Count; j++) {
                statements.Add(body.Statements[j]);
            }

            // Strip empty statements (comments are trivia, not statements)
            var nonEmpty = statements
                .Where(s => !(s is EmptyStatementSyntax))
                .ToList();

            // Determine what return statement to map this label to
            ReturnStatementSyntax replacementReturn = null;

            if (nonEmpty.Count == 0) {
                // Empty label or only empty statements → replace goto with return;
                replacementReturn = ReturnStatement();
            }
            else if (nonEmpty.Count == 1 && nonEmpty[0] is ReturnStatementSyntax returnStmt) {
                // Single return statement → replace goto with that return
                replacementReturn = returnStmt;
            }
            // Otherwise: more statements follow, so don't collapse

            if (replacementReturn != null) {
                // Map the outermost label (and all nested labels within it) to the same return
                CollectAllLabels(labeled, label => {
                    map[label.Text] = replacementReturn;
                });
            }
        }

        // If no labels were collapsible, return the body unchanged
        if (map.Count == 0) {
            return body;
        }

        // Apply the replacer to the whole body
        return (BlockSyntax)new GotoReplacer(map).Visit(body);
    }

    /// <summary>
    /// Recursively unwraps nested labeled statements to get to the underlying (non-labeled) statement.
    /// </summary>
    private static StatementSyntax UnwrapNestedLabels(StatementSyntax stmt)
    {
        while (stmt is LabeledStatementSyntax labeled) {
            stmt = labeled.Statement;
        }
        return stmt;
    }

    /// <summary>
    /// Recursively collects all labels from a nested labeled statement structure and applies an action to each.
    /// </summary>
    private static void CollectAllLabels(LabeledStatementSyntax labeled, Action<SyntaxToken> action)
    {
        action(labeled.Identifier);
        if (labeled.Statement is LabeledStatementSyntax nestedLabeled) {
            CollectAllLabels(nestedLabeled, action);
        }
    }

    /// <summary>
    /// Inner rewriter that replaces goto statements with their mapped return statements.
    /// </summary>
    private class GotoReplacer : LoggedRewriter
    {
        private readonly Dictionary<string, ReturnStatementSyntax> _map;

        public GotoReplacer(Dictionary<string, ReturnStatementSyntax> map)
        {
            _map = map;
        }

        public override SyntaxNode VisitGotoStatement(GotoStatementSyntax node)
        {
            // Only replace if it's a plain goto (not goto case or goto default)
            if (!node.IsKind(SyntaxKind.GotoStatement)) {
                return base.VisitGotoStatement(node);
            }

            // Check if the label expression is an identifier that's in our map
            if (node.Expression is IdentifierNameSyntax idName
                && _map.TryGetValue(idName.Identifier.Text, out var replacementReturn)) {

                // Replace with the mapped return, preserving trivia from the original goto
                return replacementReturn.WithTriviaFrom(node);
            }

            return base.VisitGotoStatement(node);
        }
    }
}
