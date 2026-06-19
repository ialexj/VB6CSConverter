using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Collapses locals declared with default/null (or uninitialized) into their first assignment.
/// It can also move the declaration to a nested block when all symbol usages are contained there.
/// </summary>
public class LocalDeclarationCollapseRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitBlock(BlockSyntax node)
        => Rewrite(node, node => {
            // Perform at most one structural change per pass; Program's rewriter loop will rerun.
            var split = SplitFirstMultiDeclarator(node);
            if (split is not null) {
                return base.VisitBlock(split);
            }

            foreach (var declaration in node.Statements.OfType<LocalDeclarationStatementSyntax>()) {
                if (TryCollapseDeclaration(node, declaration, out var updated)) {
                    return base.VisitBlock(updated);
                }
            }

            return base.VisitBlock(node);
        });

    private bool TryCollapseDeclaration(
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        out BlockSyntax updated)
    {
        updated = block;

        var declarator = declaration.Declaration.Variables.FirstOrDefault(IsEligibleDeclaration);
        if (declarator is null) {
            return false;
        }

        if (semantics.GetDeclaredSymbol(declarator) is not ILocalSymbol symbol) {
            return false;
        }

        if (!TryFindFirstAssignment(block, declaration, symbol, out var assignment, out var assignmentStatement, out var assignmentBlock)) {
            return false;
        }

        var refs = GetSymbolReferences(block, symbol);

        if (refs.Any(r => r.SpanStart < assignment.SpanStart)) {
            return false;
        }

        if (assignmentBlock == block) {
            updated = CollapseWithinSameBlock(block, declaration, declarator, assignmentStatement, assignment.Right);
            return true;
        }

        // Only move into nested scope when every symbol use is contained in that nested block.
        if (refs.Any(r => !assignmentBlock.Span.Contains(r.Span))) {
            return false;
        }

        updated = MoveDeclarationToNestedBlock(block, declaration, declarator, assignmentStatement, assignment.Right);
        return true;
    }

    private static bool IsEligibleDeclaration(VariableDeclaratorSyntax declarator)
    {
        if (declarator.Initializer is null) {
            return true;
        }

        return declarator.Initializer.Value.Kind() is
            SyntaxKind.DefaultLiteralExpression or
            SyntaxKind.DefaultExpression or
            SyntaxKind.NullLiteralExpression;
    }

    private bool TryFindFirstAssignment(
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        ILocalSymbol symbol,
        out AssignmentExpressionSyntax assignment,
        out ExpressionStatementSyntax assignmentStatement,
        out BlockSyntax assignmentBlock)
    {
        assignment = null;
        assignmentStatement = null;
        assignmentBlock = null;

        var candidates = block.DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.SimpleAssignmentExpression))
            .Where(a => a.SpanStart > declaration.SpanStart)
            .OrderBy(a => a.SpanStart);

        foreach (var candidate in candidates) {
            if (!SymbolEqualityComparer.Default.Equals(GetAssignedSymbol(candidate.Left), symbol)) {
                continue;
            }

            if (semantics.GetEnclosingSymbol(candidate.SpanStart) is not ISymbol enclosing
                || !SymbolEqualityComparer.Default.Equals(enclosing, symbol.ContainingSymbol)) {
                continue;
            }

            var statement = candidate.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            if (statement?.Expression != candidate) {
                continue;
            }

            var containingBlock = statement.Parent as BlockSyntax;
            if (containingBlock is null) {
                continue;
            }

            assignment = candidate;
            assignmentStatement = statement;
            assignmentBlock = containingBlock;
            return true;
        }

        return false;
    }

    private ISymbol GetAssignedSymbol(ExpressionSyntax left)
    {
        return left switch
        {
            IdentifierNameSyntax id => semantics.GetSymbolInfo(id).Symbol,
            MemberAccessExpressionSyntax member => semantics.GetSymbolInfo(member).Symbol,
            ElementAccessExpressionSyntax element => semantics.GetSymbolInfo(element.Expression).Symbol,
            _ => semantics.GetSymbolInfo(left).Symbol
        };
    }

    private List<IdentifierNameSyntax> GetSymbolReferences(BlockSyntax block, ILocalSymbol symbol)
        => block.DescendantNodes(descendIntoChildren: _ => true)
            .OfType<IdentifierNameSyntax>()
            .Where(id => SymbolEqualityComparer.Default.Equals(semantics.GetSymbolInfo(id).Symbol, symbol))
            .ToList();

    private BlockSyntax CollapseWithinSameBlock(
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        VariableDeclaratorSyntax declarator,
        ExpressionStatementSyntax assignmentStatement,
        ExpressionSyntax right)
    {
        var tracked = block.TrackNodes(declaration, assignmentStatement);
        var currentDeclaration = tracked.GetCurrentNode(declaration)!;
        var currentAssignment = tracked.GetCurrentNode(assignmentStatement)!;

        StatementSyntax replacement;
        if (currentDeclaration.Declaration.Variables.Count == 1) {
            var collapsedDeclarator = currentDeclaration.Declaration.Variables[0]
                .WithInitializer(EqualsValueClause(right));

            replacement = currentDeclaration.WithDeclaration(
                currentDeclaration.Declaration.WithVariables(SingletonSeparatedList(collapsedDeclarator)));
        }
        else {
            // Defensive path: if a multi-declarator reaches here, split while collapsing only the target symbol.
            var statements = currentDeclaration.Declaration.Variables.Select(v => {
                var isTarget = v.Identifier.Text == declarator.Identifier.Text;
                var dv = isTarget ? v.WithInitializer(EqualsValueClause(right)) : v;
                return (StatementSyntax)currentDeclaration.WithDeclaration(
                    currentDeclaration.Declaration.WithVariables(SingletonSeparatedList(dv)));
            }).ToList();

            tracked = tracked.ReplaceNode(currentDeclaration, statements);
            var assignmentAfterSplit = tracked.GetCurrentNode(assignmentStatement)!;
            return tracked.RemoveNode(assignmentAfterSplit, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        tracked = tracked.ReplaceNode(currentDeclaration, replacement);
        var assignmentAfterReplace = tracked.GetCurrentNode(assignmentStatement)!;

        return tracked.RemoveNode(assignmentAfterReplace, SyntaxRemoveOptions.KeepNoTrivia)!;
    }

    private BlockSyntax MoveDeclarationToNestedBlock(
        BlockSyntax rootBlock,
        LocalDeclarationStatementSyntax declaration,
        VariableDeclaratorSyntax declarator,
        ExpressionStatementSyntax assignmentStatement,
        ExpressionSyntax right)
    {
        var tracked = rootBlock.TrackNodes(declaration, assignmentStatement);

        var currentDeclaration = tracked.GetCurrentNode(declaration)!;
        var currentAssignment = tracked.GetCurrentNode(assignmentStatement)!;
        var currentTargetBlock = (BlockSyntax)currentAssignment.Parent!;

        var movedDeclarator = VariableDeclarator(declarator.Identifier)
            .WithInitializer(EqualsValueClause(right));

        // Build a completely fresh node — if we use With* on currentDeclaration, Roslyn copies
        // its tracking annotation into the new node, causing GetCurrentNode to find two matches.
        var movedDeclaration = LocalDeclarationStatement(
                currentDeclaration.AttributeLists,
                currentDeclaration.Modifiers,
                currentDeclaration.Declaration.WithVariables(SingletonSeparatedList(movedDeclarator)))
            .WithLeadingTrivia(currentAssignment.GetLeadingTrivia())
            .WithTrailingTrivia(currentAssignment.GetTrailingTrivia());

        var replacedTarget = currentTargetBlock.ReplaceNode(currentAssignment, movedDeclaration);
        tracked = tracked.ReplaceNode(currentTargetBlock, replacedTarget);

        var declarationAfterMove = tracked.GetCurrentNode(declaration)!;
        return tracked.RemoveNode(declarationAfterMove, SyntaxRemoveOptions.KeepNoTrivia)!;
    }

    private static BlockSyntax SplitFirstMultiDeclarator(BlockSyntax block)
    {
        for (var i = 0; i < block.Statements.Count; i++) {
            if (block.Statements[i] is not LocalDeclarationStatementSyntax local || local.Declaration.Variables.Count <= 1) {
                continue;
            }

            var splitStatements = local.Declaration.Variables.Select((variable, index) => {
                var split = local.WithDeclaration(local.Declaration.WithVariables(SingletonSeparatedList(variable)));
                if (index == 0) {
                    split = split.WithLeadingTrivia(local.GetLeadingTrivia());
                }
                else {
                    split = split.WithLeadingTrivia();
                }

                if (index == local.Declaration.Variables.Count - 1) {
                    split = split.WithTrailingTrivia(local.GetTrailingTrivia());
                }
                else {
                    split = split.WithTrailingTrivia();
                }

                return (StatementSyntax)split;
            }).ToArray();

            var statements = block.Statements.RemoveAt(i).InsertRange(i, splitStatements);
            return block.WithStatements(statements);
        }

        return null;
    }
}
