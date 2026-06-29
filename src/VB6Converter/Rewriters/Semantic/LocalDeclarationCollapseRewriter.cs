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
/// NOTE: this rewriter has unresolved issues - do not use it as reference for other rewriters.
/// </summary>
public class LocalDeclarationCollapseRewriter(SemanticModel semantics) : LoggedRewriter
{
    private enum PlanKind
    {
        Split,
        CollapseSameBlock,
        MoveToNestedBlock
    }

    private sealed record Plan(
        PlanKind Kind,
        SyntaxAnnotation Block,
        SyntaxAnnotation Declaration,
        SyntaxAnnotation Declarator,
        SyntaxAnnotation AssignmentStatement,
        SyntaxAnnotation Assignment,
        int Order);

    private sealed record PendingPlan(
        PlanKind Kind,
        BlockSyntax Block,
        LocalDeclarationStatementSyntax Declaration,
        VariableDeclaratorSyntax Declarator,
        ExpressionStatementSyntax AssignmentStatement,
        AssignmentExpressionSyntax Assignment,
        int Order);

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
        => Rewrite(node, node => {
            if (!IsInSemanticTree(node)) {
                return node;
            }

            var pending = CollectPlans(node);
            if (pending.Count == 0) {
                return node;
            }

            var (annotatedRoot, plans) = AnnotatePlans(node, pending);
            // Keep one structural rewrite per pass; pipeline reruns until convergence.
            return ApplyFirstPlan(annotatedRoot, plans[0]);
        });

    private List<PendingPlan> CollectPlans(CompilationUnitSyntax root)
    {
        List<PendingPlan> plans = [];

        foreach (var block in root.DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                     .OfType<BlockSyntax>()) {
            var split = FindFirstMultiDeclarator(block);
            if (split is not null) {
                plans.Add(new PendingPlan(
                    PlanKind.Split,
                    block,
                    split,
                    split.Declaration.Variables[0],
                    null!,
                    null!,
                    split.SpanStart));
                continue;
            }

            foreach (var declaration in block.Statements.OfType<LocalDeclarationStatementSyntax>()) {
                if (!TryGetCollapsePlan(block, declaration, out var plan)) {
                    continue;
                }

                plans.Add(plan);
                break;
            }
        }

        return plans.OrderBy(p => p.Order).ToList();
    }

    private bool TryGetCollapsePlan(
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        out PendingPlan plan)
    {
        plan = null!;

        if (!IsInSemanticTree(declaration)) {
            return false;
        }

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
            plan = new PendingPlan(
                PlanKind.CollapseSameBlock,
                block,
                declaration,
                declarator,
                assignmentStatement,
                assignment,
                declaration.SpanStart);
            return true;
        }

        if (refs.Any(r => !assignmentBlock.Span.Contains(r.Span))) {
            return false;
        }

        plan = new PendingPlan(
            PlanKind.MoveToNestedBlock,
            block,
            declaration,
            declarator,
            assignmentStatement,
            assignment,
            declaration.SpanStart);
        return true;
    }

    private static (CompilationUnitSyntax AnnotatedRoot, List<Plan> Plans) AnnotatePlans(
        CompilationUnitSyntax root,
        IEnumerable<PendingPlan> pending)
    {
        var blockAnnotations = new Dictionary<BlockSyntax, SyntaxAnnotation>();
        var declarationAnnotations = new Dictionary<LocalDeclarationStatementSyntax, SyntaxAnnotation>();
        var declaratorAnnotations = new Dictionary<VariableDeclaratorSyntax, SyntaxAnnotation>();
        var assignmentStatementAnnotations = new Dictionary<ExpressionStatementSyntax, SyntaxAnnotation>();
        var assignmentAnnotations = new Dictionary<AssignmentExpressionSyntax, SyntaxAnnotation>();
        var nodeAnnotations = new Dictionary<SyntaxNode, List<SyntaxAnnotation>>();

        static void AddAnnotation(Dictionary<SyntaxNode, List<SyntaxAnnotation>> map, SyntaxNode node, SyntaxAnnotation annotation)
        {
            if (node is null) {
                return;
            }

            if (!map.TryGetValue(node, out var list)) {
                list = [];
                map[node] = list;
            }

            list.Add(annotation);
        }

        foreach (var item in pending) {
            if (!blockAnnotations.ContainsKey(item.Block)) {
                blockAnnotations[item.Block] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.Block, blockAnnotations[item.Block]);
            }

            if (!declarationAnnotations.ContainsKey(item.Declaration)) {
                declarationAnnotations[item.Declaration] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.Declaration, declarationAnnotations[item.Declaration]);
            }

            if (!declaratorAnnotations.ContainsKey(item.Declarator)) {
                declaratorAnnotations[item.Declarator] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.Declarator, declaratorAnnotations[item.Declarator]);
            }

            if (item.AssignmentStatement is not null && !assignmentStatementAnnotations.ContainsKey(item.AssignmentStatement)) {
                assignmentStatementAnnotations[item.AssignmentStatement] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.AssignmentStatement, assignmentStatementAnnotations[item.AssignmentStatement]);
            }

            if (item.Assignment is not null && !assignmentAnnotations.ContainsKey(item.Assignment)) {
                assignmentAnnotations[item.Assignment] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.Assignment, assignmentAnnotations[item.Assignment]);
            }
        }

        var updated = nodeAnnotations.Count == 0
            ? root
            : root.ReplaceNodes(nodeAnnotations.Keys,
                (original, rewritten) => rewritten.WithAdditionalAnnotations(nodeAnnotations[original].ToArray()));

        var plans = pending.Select(item => new Plan(
            item.Kind,
            blockAnnotations[item.Block],
            declarationAnnotations[item.Declaration],
            declaratorAnnotations[item.Declarator],
            item.AssignmentStatement is null ? null : assignmentStatementAnnotations[item.AssignmentStatement],
            item.Assignment is null ? null : assignmentAnnotations[item.Assignment],
            item.Order)).ToList();

        return ((CompilationUnitSyntax)updated, plans);
    }

    private static SyntaxNode ApplyFirstPlan(CompilationUnitSyntax root, Plan plan)
    {
        if (GetAnnotatedNode<BlockSyntax>(root, plan.Block) is not BlockSyntax block) {
            return root;
        }

        return plan.Kind switch
        {
            PlanKind.Split => ApplySplit(root, block),
            PlanKind.CollapseSameBlock => ApplyCollapseSameBlock(root, block, plan),
            PlanKind.MoveToNestedBlock => ApplyMoveToNestedBlock(root, block, plan),
            _ => root
        };
    }

    private static SyntaxNode ApplySplit(CompilationUnitSyntax root, BlockSyntax block)
    {
        var split = SplitFirstMultiDeclarator(block);
        return split is null ? root : root.ReplaceNode(block, split);
    }

    private static SyntaxNode ApplyCollapseSameBlock(CompilationUnitSyntax root, BlockSyntax block, Plan plan)
    {
        var declaration = GetAnnotatedNode<LocalDeclarationStatementSyntax>(root, plan.Declaration);
        var declarator = GetAnnotatedNode<VariableDeclaratorSyntax>(root, plan.Declarator);
        var assignmentStatement = GetAnnotatedNode<ExpressionStatementSyntax>(root, plan.AssignmentStatement);
        var assignment = GetAnnotatedNode<AssignmentExpressionSyntax>(root, plan.Assignment);

        if (declaration is null || declarator is null || assignmentStatement is null || assignment is null) {
            return root;
        }

        var rewrittenBlock = CollapseWithinSameBlock(block, declaration, declarator, assignmentStatement, assignment.Right);
        return root.ReplaceNode(block, rewrittenBlock);
    }

    private static SyntaxNode ApplyMoveToNestedBlock(CompilationUnitSyntax root, BlockSyntax block, Plan plan)
    {
        var declaration = GetAnnotatedNode<LocalDeclarationStatementSyntax>(root, plan.Declaration);
        var declarator = GetAnnotatedNode<VariableDeclaratorSyntax>(root, plan.Declarator);
        var assignmentStatement = GetAnnotatedNode<ExpressionStatementSyntax>(root, plan.AssignmentStatement);
        var assignment = GetAnnotatedNode<AssignmentExpressionSyntax>(root, plan.Assignment);

        if (declaration is null || declarator is null || assignmentStatement is null || assignment is null) {
            return root;
        }

        var rewrittenBlock = MoveDeclarationToNestedBlock(block, declaration, declarator, assignmentStatement, assignment.Right);
        return root.ReplaceNode(block, rewrittenBlock);
    }

    private static T GetAnnotatedNode<T>(SyntaxNode root, SyntaxAnnotation annotation)
        where T : SyntaxNode
        => annotation is null ? null : root.GetAnnotatedNodes(annotation).OfType<T>().FirstOrDefault();

    private bool IsInSemanticTree(SyntaxNode node)
        => ReferenceEquals(node?.SyntaxTree, semantics.SyntaxTree);

    private static LocalDeclarationStatementSyntax FindFirstMultiDeclarator(BlockSyntax block)
        => block.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .FirstOrDefault(local => local.Declaration.Variables.Count > 1);

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

    private static BlockSyntax CollapseWithinSameBlock(
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

    private static BlockSyntax MoveDeclarationToNestedBlock(
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
