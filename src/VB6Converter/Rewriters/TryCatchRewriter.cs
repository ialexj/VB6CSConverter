using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class TryCatchRewriter : LoggedRewriter
{
    enum ScanState { Before, Try, Catch };

    public static readonly TryCatchRewriter Default = new();

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var rewritten = RewriteOnErrorBlocks(node);
        return base.VisitBlock(rewritten);
    }

    private static BlockSyntax RewriteOnErrorBlocks(BlockSyntax node)
    {
        var current = node;

        while (TryRewriteFirstOnError(current, out var next)) {
            current = next;
        }

        return current;
    }

    private static bool TryRewriteFirstOnError(BlockSyntax node, out BlockSyntax rewritten)
    {
        rewritten = node;

        if (!TryGetFirstOnError(node, out var onErrorGoto, out var onErrorIndex, out var errLabelName)) {
            return false;
        }

        var errLabelIndex = FindLabelIndex(node, errLabelName, onErrorIndex + 1);
        if (errLabelIndex < 0) {
            return false;
        }

        var errLabeledStmt = (LabeledStatementSyntax)node.Statements[errLabelIndex];
        var (beforeStatements, tryStatements, catchStatements) = Partition(node, onErrorGoto, errLabeledStmt);

        // Preserve current inline-catch behavior for simple/structured control flow.
        if (TryBuildInlineTryCatch(beforeStatements, tryStatements, catchStatements, out var inlineRewritten)) {
            rewritten = inlineRewritten;
            return true;
        }

        // General fallback: keep handler labels at root scope and jump to the handler from catch.
        var fallbackRewritten = BuildGotoCatchFallback(node, errLabelIndex, errLabelName, beforeStatements, tryStatements);
        if (fallbackRewritten != node) {
            rewritten = fallbackRewritten;
            return true;
        }

        return false;
    }

    private static bool TryGetFirstOnError(BlockSyntax node, out StatementSyntax onErrorGoto, out int index, out string errLabelName)
    {
        for (var i = 0; i < node.Statements.Count; i++) {
            var stmt = node.Statements[i];
            if (!stmt.HasAnnotations("OnErrorGoto")) {
                continue;
            }

            var annotation = stmt.GetAnnotations("OnErrorGoto").FirstOrDefault();
            if (annotation?.Data is null) {
                continue;
            }

            onErrorGoto = stmt;
            index = i;
            errLabelName = annotation.Data;
            return true;
        }

        onErrorGoto = null!;
        index = -1;
        errLabelName = string.Empty;
        return false;
    }

    private static int FindLabelIndex(BlockSyntax node, string labelName, int startIndex)
    {
        for (var i = startIndex; i < node.Statements.Count; i++) {
            if (node.Statements[i] is LabeledStatementSyntax labeled
                && string.Equals(labeled.Identifier.Text, labelName, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }

    private static (List<StatementSyntax> Before, List<StatementSyntax> Try, List<StatementSyntax> Catch) Partition(
        BlockSyntax node,
        StatementSyntax onErrorGoto,
        LabeledStatementSyntax errLabeledStmt)
    {
        List<StatementSyntax> beforeStatements = [];
        List<StatementSyntax> tryStatements = [];
        List<StatementSyntax> catchStatements = [];

        var region = ScanState.Before;
        foreach (var stmt in node.Statements) {
            if (stmt == onErrorGoto) {
                region = ScanState.Try;
                continue;
            }

            if (stmt == errLabeledStmt) {
                region = ScanState.Catch;
                catchStatements.Add(errLabeledStmt.Statement);
                continue;
            }

            switch (region) {
                case ScanState.Before:
                    beforeStatements.Add(stmt);
                    break;
                case ScanState.Try:
                    tryStatements.Add(stmt);
                    break;
                case ScanState.Catch:
                    catchStatements.Add(stmt);
                    break;
            }
        }

        return (beforeStatements, tryStatements, catchStatements);
    }

    private static bool TryBuildInlineTryCatch(
        List<StatementSyntax> beforeStatements,
        List<StatementSyntax> tryStatements,
        List<StatementSyntax> catchStatements,
        out BlockSyntax rewritten)
    {
        rewritten = null!;

        var exitLabelNames = tryStatements
            .OfType<LabeledStatementSyntax>()
            .Select(l => l.Identifier.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allGotos = tryStatements.Concat(catchStatements)
            .SelectMany(s => s.DescendantNodesAndSelf().OfType<GotoStatementSyntax>())
            .ToList();

        foreach (var gotoStmt in allGotos) {
            var target = gotoStmt.Expression?.WithoutTrivia().ToString() ?? "";
            if (!exitLabelNames.Contains(target)) {
                return false;
            }
        }

        var exitIdx = tryStatements.FindIndex(
            s => s is LabeledStatementSyntax l && exitLabelNames.Contains(l.Identifier.Text));

        List<StatementSyntax> tryBody;
        List<StatementSyntax> exitCluster;

        if (exitIdx < 0) {
            tryBody = tryStatements;
            exitCluster = [];
        }
        else {
            tryBody = tryStatements.Take(exitIdx).ToList();
            exitCluster = tryStatements.Skip(exitIdx).ToList();
        }

        var tryStatement = TryStatement(
            Block(tryBody),
            SingletonList(CatchClause(null, null, Block(catchStatements))),
            default
        );

        rewritten = Block((StatementSyntax[])[.. beforeStatements, tryStatement, .. exitCluster]);
        return true;
    }

    private static BlockSyntax BuildGotoCatchFallback(
        BlockSyntax original,
        int errLabelIndex,
        string errLabelName,
        List<StatementSyntax> beforeStatements,
        List<StatementSyntax> tryStatements)
    {
        var firstLabelInTryIndex = tryStatements.FindIndex(s => s is LabeledStatementSyntax);

        List<StatementSyntax> tryBody;
        List<StatementSyntax> exitCluster;

        if (firstLabelInTryIndex < 0) {
            tryBody = tryStatements;
            exitCluster = [];
        }
        else {
            tryBody = tryStatements.Take(firstLabelInTryIndex).ToList();
            exitCluster = tryStatements.Skip(firstLabelInTryIndex).ToList();
        }

        var remainder = original.Statements.Skip(errLabelIndex).ToList();

        var catchGoto = GotoStatement(SyntaxKind.GotoStatement, IdentifierName(errLabelName));
        var tryStatement = TryStatement(
            Block(tryBody),
            SingletonList(CatchClause(null, null, Block(catchGoto))),
            default
        );

        return Block((StatementSyntax[])[.. beforeStatements, tryStatement, .. exitCluster, .. remainder]);
    }
}
