using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class TryCatchRewriter : LoggedRewriter
{
    enum ScanState { Before, Try, Catch };

    public static readonly TryCatchRewriter Default = new();

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var onErrorGoto = node.GetAnnotatedNodes("OnErrorGoto").FirstOrDefault();
        if (onErrorGoto != null) {
            // Only convert if the label is the next label, to avoid jump backs
            var catchLabel = onErrorGoto.GetAnnotations("OnErrorGoto").First().Data;
            
            var labeled = onErrorGoto.Parent.ChildNodes().OfType<LabeledStatementSyntax>().FirstOrDefault();
            if (labeled is null) {
                return node;
            }

            if (onErrorGoto.Parent.DescendantNodes().OfType<GotoStatementSyntax>().Any()) {
                // don't
                return node;
            }
            
            if (string.Equals(labeled.Identifier.Text, catchLabel)) {
                List<StatementSyntax> beforeStatements = [];
                List<StatementSyntax> tryStatements = [];
                List<StatementSyntax> catchStatements = [];

                var state = ScanState.Before;

                foreach (var statement in node.Statements) {
                    if (statement == onErrorGoto) {
                        state = ScanState.Try;
                    }
                    else if (statement == labeled) {
                        state = ScanState.Catch;
                        catchStatements.Add(labeled.Statement);
                    }
                    else if (state == ScanState.Before) {
                        beforeStatements.Add(statement);
                    }
                    else if (state == ScanState.Try) {
                        tryStatements.Add(statement);
                    }
                    else if (state == ScanState.Catch) {
                        catchStatements.Add(statement);
                    }
                }

                var tryStatement = TryStatement(
                    Block(tryStatements),
                    SingletonList(
                        CatchClause(null, null, Block(catchStatements))
                    ),
                    default
                );

                return Block(beforeStatements.Concat([tryStatement]));
            }
        }

        return base.VisitBlock(node);
    }
}
