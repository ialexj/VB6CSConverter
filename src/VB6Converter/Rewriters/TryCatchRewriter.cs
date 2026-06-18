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
        var onErrorGoto = node.GetAnnotatedNodes("OnErrorGoto").FirstOrDefault();
        if (onErrorGoto is null)
            return base.VisitBlock(node);

        var errLabelName = onErrorGoto.GetAnnotations("OnErrorGoto").First().Data!;

        // Find the error-handler labeled statement by name (not just the first label in the block)
        var errLabeledStmt = node.Statements
            .OfType<LabeledStatementSyntax>()
            .FirstOrDefault(l => string.Equals(l.Identifier.Text, errLabelName, StringComparison.OrdinalIgnoreCase));

        if (errLabeledStmt is null)
            return base.VisitBlock(node);

        // Split the block into: before the OnError, the try region, and the catch region
        List<StatementSyntax> beforeStatements = [];
        List<StatementSyntax> tryStatements = [];
        List<StatementSyntax> catchStatements = [];

        var region = ScanState.Before;
        foreach (var stmt in node.Statements) {
            if (stmt == onErrorGoto) {
                region = ScanState.Try;
                continue; // the OnError statement itself is consumed
            }
            if (stmt == errLabeledStmt) {
                region = ScanState.Catch;
                catchStatements.Add(errLabeledStmt.Statement); // add body, strip the label
                continue;
            }
            switch (region) {
                case ScanState.Before: beforeStatements.Add(stmt); break;
                case ScanState.Try:    tryStatements.Add(stmt);    break;
                case ScanState.Catch:  catchStatements.Add(stmt);  break;
            }
        }

        // "Exit labels" are labeled statements inside the try region (e.g. DeleteRegValue_End).
        // They act as normal-exit points reached via GoTo from within the try block or via Resume
        // from within the catch block.  In C# it is legal to goto OUT of a try/catch to an
        // external label, so we move these labels (and everything between them and the error label)
        // to after the try/catch.
        var exitLabelNames = tryStatements
            .OfType<LabeledStatementSyntax>()
            .Select(l => l.Identifier.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every goto in the try+catch region must target an exit label; anything else means the
        // control flow is too complex to restructure safely.
        var allGotos = tryStatements.Concat(catchStatements)
            .SelectMany(s => s.DescendantNodesAndSelf().OfType<GotoStatementSyntax>())
            .ToList();

        foreach (var gotoStmt in allGotos) {
            var target = gotoStmt.Expression?.WithoutTrivia().ToString() ?? "";
            if (!exitLabelNames.Contains(target))
                return base.VisitBlock(node);
        }

        // Split the try region at the first exit label:
        //   - statements before it  → the actual try body
        //   - the exit label and everything after it (up to the error label) → placed after try/catch
        var exitIdx = tryStatements.FindIndex(
            s => s is LabeledStatementSyntax l && exitLabelNames.Contains(l.Identifier.Text));

        List<StatementSyntax> tryBody;
        List<StatementSyntax> exitCluster;

        if (exitIdx < 0) {
            // No exit labels — plain try/catch (original simple case)
            tryBody = tryStatements;
            exitCluster = [];
        }
        else {
            tryBody    = tryStatements.Take(exitIdx).ToList();
            exitCluster = tryStatements.Skip(exitIdx).ToList();
        }

        var tryStatement = TryStatement(
            Block(tryBody),
            SingletonList(CatchClause(null, null, Block(catchStatements))),
            default
        );

        return Block((StatementSyntax[])[.. beforeStatements, tryStatement, .. exitCluster]);
    }
}
