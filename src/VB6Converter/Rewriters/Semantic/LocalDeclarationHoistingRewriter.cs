using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Hoists locals declared in nested scopes when they are referenced outside those scopes.
/// Only declarations with constant/default-style initializers are moved.
/// </summary>
public class LocalDeclarationHoistingRewriter(SemanticModel semantics) : LoggedRewriter
{
    private enum PlanKind
    {
        Split,
        Hoist
    }

    private sealed record Plan(
        PlanKind Kind,
        SyntaxAnnotation Declaration,
        SyntaxAnnotation DeclarationBlock,
        SyntaxAnnotation Anchor,
        int Depth,
        int Order);

    private sealed record PendingPlan(
        PlanKind Kind,
        LocalDeclarationStatementSyntax Declaration,
        BlockSyntax DeclarationBlock,
        StatementSyntax Anchor,
        int Depth,
        int Order);

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
        => Rewrite(node, node => {
            if (!ReferenceEquals(node.SyntaxTree, semantics.SyntaxTree)) {
                return node;
            }

            var pending = CollectPlans(node);
            if (pending.Count == 0) {
                return node;
            }

            var (annotatedRoot, plans) = AnnotatePlans(node, pending);
            SyntaxNode updated = annotatedRoot;

            foreach (var plan in plans.OrderByDescending(p => p.Depth).ThenByDescending(p => p.Order)) {
                updated = plan.Kind switch
                {
                    PlanKind.Split => ApplySplit(updated, plan),
                    PlanKind.Hoist => ApplyHoist(updated, plan),
                    _ => updated
                };
            }

            return updated;
        });

    private List<PendingPlan> CollectPlans(CompilationUnitSyntax root)
    {
        List<PendingPlan> plans = [];

        foreach (var declaration in GetNestedDeclarations(root)) {
            if (declaration.Parent is not BlockSyntax declarationBlock) {
                continue;
            }

            if (declarationBlock.Ancestors().OfType<BlockSyntax>().FirstOrDefault() is not BlockSyntax outerBlock) {
                continue;
            }

            if (!TryFindHoistCandidate(declaration, declarationBlock)) {
                continue;
            }

            var depth = declarationBlock.AncestorsAndSelf().OfType<BlockSyntax>().Count();
            if (declaration.Declaration.Variables.Count > 1) {
                plans.Add(new PendingPlan(
                    PlanKind.Split,
                    declaration,
                    declarationBlock,
                    declaration,
                    depth,
                    declaration.SpanStart));
                continue;
            }

            var anchor = GetHoistAnchor(declarationBlock, outerBlock);
            if (anchor is null) {
                continue;
            }

            plans.Add(new PendingPlan(
                PlanKind.Hoist,
                declaration,
                declarationBlock,
                anchor,
                depth,
                declaration.SpanStart));
        }

        return plans;
    }

    private static IEnumerable<LocalDeclarationStatementSyntax> GetNestedDeclarations(SyntaxNode root)
        => root.DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(d => d.Parent is BlockSyntax declarationBlock && declarationBlock.Ancestors().OfType<BlockSyntax>().Any());

    private static (CompilationUnitSyntax AnnotatedRoot, List<Plan> Plans) AnnotatePlans(
        CompilationUnitSyntax root,
        IEnumerable<PendingPlan> pending)
    {
        var declarationAnnotations = new Dictionary<LocalDeclarationStatementSyntax, SyntaxAnnotation>();
        var blockAnnotations = new Dictionary<BlockSyntax, SyntaxAnnotation>();
        var statementAnnotations = new Dictionary<StatementSyntax, SyntaxAnnotation>();
        var nodeAnnotations = new Dictionary<SyntaxNode, List<SyntaxAnnotation>>();

        static void AddAnnotation(Dictionary<SyntaxNode, List<SyntaxAnnotation>> map, SyntaxNode node, SyntaxAnnotation annotation)
        {
            if (!map.TryGetValue(node, out var annotations)) {
                annotations = [];
                map[node] = annotations;
            }

            annotations.Add(annotation);
        }

        foreach (var item in pending) {
            if (!declarationAnnotations.ContainsKey(item.Declaration)) {
                declarationAnnotations[item.Declaration] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.Declaration, declarationAnnotations[item.Declaration]);
            }

            if (!blockAnnotations.ContainsKey(item.DeclarationBlock)) {
                blockAnnotations[item.DeclarationBlock] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.DeclarationBlock, blockAnnotations[item.DeclarationBlock]);
            }

            if (!statementAnnotations.ContainsKey(item.Anchor)) {
                statementAnnotations[item.Anchor] = new SyntaxAnnotation();
                AddAnnotation(nodeAnnotations, item.Anchor, statementAnnotations[item.Anchor]);
            }
        }

        var updated = nodeAnnotations.Count == 0
            ? root
            : root.ReplaceNodes(nodeAnnotations.Keys,
                (original, rewritten) => rewritten.WithAdditionalAnnotations(nodeAnnotations[original].ToArray()));

        var plans = pending.Select(item => new Plan(
            item.Kind,
            declarationAnnotations[item.Declaration],
            blockAnnotations[item.DeclarationBlock],
            statementAnnotations[item.Anchor],
            item.Depth,
            item.Order)).ToList();

        return ((CompilationUnitSyntax)updated, plans);
    }

    private static SyntaxNode ApplySplit(SyntaxNode root, Plan plan)
    {
        if (GetAnnotatedNode<LocalDeclarationStatementSyntax>(root, plan.Declaration) is not LocalDeclarationStatementSyntax declaration) {
            return root;
        }

        return SplitDeclaration(root, declaration);
    }

    private static SyntaxNode ApplyHoist(SyntaxNode root, Plan plan)
    {
        if (GetAnnotatedNode<LocalDeclarationStatementSyntax>(root, plan.Declaration) is not LocalDeclarationStatementSyntax declaration) {
            return root;
        }

        if (GetAnnotatedNode<BlockSyntax>(root, plan.DeclarationBlock) is not BlockSyntax declarationBlock) {
            return root;
        }

        if (GetAnnotatedNode<StatementSyntax>(root, plan.Anchor) is not StatementSyntax anchor) {
            return root;
        }

        return HoistDeclaration(root, declaration, declarationBlock, anchor);
    }

    private static T GetAnnotatedNode<T>(SyntaxNode root, SyntaxAnnotation annotation)
        where T : SyntaxNode
        => root.GetAnnotatedNodes(annotation).OfType<T>().FirstOrDefault();

    private bool TryFindHoistCandidate(
        LocalDeclarationStatementSyntax declaration,
        BlockSyntax declarationBlock)
    {
        foreach (var candidate in declaration.Declaration.Variables) {
            if (!IsEligibleDeclaration(candidate)) {
                continue;
            }

            if (semantics.GetDeclaredSymbol(candidate) is not ILocalSymbol localSymbol) {
                continue;
            }

            var references = GetSymbolReferences(localSymbol, declaration);
            if (!references.Any(r => !declarationBlock.Span.Contains(r.Span))) {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool IsEligibleDeclaration(VariableDeclaratorSyntax declarator)
    {
        if (declarator.Initializer is null) {
            return false;
        }

        var initializer = declarator.Initializer.Value;
        if (initializer.Kind() is SyntaxKind.DefaultLiteralExpression or SyntaxKind.DefaultExpression or SyntaxKind.NullLiteralExpression) {
            return true;
        }

        return semantics.GetConstantValue(initializer).HasValue;
    }

    private List<IdentifierNameSyntax> GetSymbolReferences(
        ILocalSymbol symbol,
        LocalDeclarationStatementSyntax declaration)
    {
        var declarationContainer = GetReferenceContainer(declaration);
        return semantics.SyntaxTree.GetRoot()
            .DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<IdentifierNameSyntax>()
            .Where(id => id.Identifier.ValueText == symbol.Name)
            .Where(id => IsSameContainer(declarationContainer, GetReferenceContainer(id)))
            .Where(id => {
                var resolved = semantics.GetSymbolInfo(id).Symbol;
                return resolved is null || SymbolEqualityComparer.Default.Equals(resolved, symbol);
            })
            .ToList();
    }

    private static SyntaxNode GetReferenceContainer(SyntaxNode node)
        => node.AncestorsAndSelf().FirstOrDefault(n
            => n is AccessorDeclarationSyntax
                or BaseMethodDeclarationSyntax
                or GlobalStatementSyntax
                or MemberDeclarationSyntax);

    private static bool IsSameContainer(SyntaxNode left, SyntaxNode right)
        => left is not null
           && right is not null
           && left.Kind() == right.Kind()
           && left.SpanStart == right.SpanStart
           && left.Span.Length == right.Span.Length;

    private static StatementSyntax GetHoistAnchor(BlockSyntax declarationBlock, BlockSyntax outerBlock)
        => declarationBlock.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => ReferenceEquals(statement.Parent, outerBlock));

    private static SyntaxNode SplitDeclaration(SyntaxNode root, LocalDeclarationStatementSyntax declaration)
    {
        var tracked = root.TrackNodes(declaration);
        var currentDeclaration = tracked.GetCurrentNode(declaration)!;

        var splitStatements = currentDeclaration.Declaration.Variables.Select((variable, index) => {
            var split = currentDeclaration.WithDeclaration(currentDeclaration.Declaration.WithVariables(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.SingletonSeparatedList(variable)));
            if (index == 0) {
                split = split.WithLeadingTrivia(currentDeclaration.GetLeadingTrivia());
            }
            else {
                split = split.WithLeadingTrivia();
            }

            if (index == currentDeclaration.Declaration.Variables.Count - 1) {
                split = split.WithTrailingTrivia(currentDeclaration.GetTrailingTrivia());
            }
            else {
                split = split.WithTrailingTrivia();
            }

            return (StatementSyntax)split;
        }).ToArray();

        return tracked.ReplaceNode(currentDeclaration, splitStatements);
    }

    private static SyntaxNode HoistDeclaration(
        SyntaxNode root,
        LocalDeclarationStatementSyntax declaration,
        BlockSyntax declarationBlock,
        StatementSyntax anchor)
    {
        var tracked = root.TrackNodes(declaration, declarationBlock, anchor);

        var currentDeclaration = tracked.GetCurrentNode(declaration)!;
        var currentDeclarationBlock = tracked.GetCurrentNode(declarationBlock)!;

        var declarationBlockWithoutLocal = currentDeclarationBlock.RemoveNode(currentDeclaration, SyntaxRemoveOptions.KeepNoTrivia)!;
        tracked = tracked.ReplaceNode(currentDeclarationBlock, declarationBlockWithoutLocal);

        var currentAnchor = tracked.GetCurrentNode(anchor)!;
        var currentOuterBlock = (BlockSyntax)currentAnchor.Parent!;
        var insertIndex = currentOuterBlock.Statements.IndexOf(currentAnchor);
        var outerStatements = currentOuterBlock.Statements.Insert(insertIndex, currentDeclaration);
        var updatedOuterBlock = currentOuterBlock.WithStatements(outerStatements);

        return tracked.ReplaceNode(currentOuterBlock, updatedOuterBlock);
    }
}
