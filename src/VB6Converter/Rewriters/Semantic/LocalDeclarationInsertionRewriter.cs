using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Inserts missing local declarations for unresolved identifiers that are first seen on
/// the left side of a simple assignment.
/// </summary>
public class LocalDeclarationInsertionRewriter(SemanticModel semantics) : LoggedRewriter
{
    private sealed record PendingPlan(
        SyntaxAnnotation Anchor,
        string Name,
        TypeSyntax Type,
        int Order);

    private sealed record GroupKey(SyntaxNode Container, string Name);

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
        => Rewrite(node, node => {
            if (!ReferenceEquals(node.SyntaxTree, semantics.SyntaxTree)) {
                return node;
            }

            var (annotated, pending) = CollectPlans(node);
            if (pending.Count == 0) {
                return node;
            }

            var updated = ApplyPlans(annotated, pending);
            return updated;
        });

    private (CompilationUnitSyntax Root, List<PendingPlan> Plans) CollectPlans(CompilationUnitSyntax root)
    {
        var unresolved = root.DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<IdentifierNameSyntax>()
            .Where(ShouldConsiderIdentifier)
            .ToList();

        var groups = unresolved
            .GroupBy(id => new GroupKey(GetReferenceContainer(id), id.Identifier.ValueText), new GroupKeyComparer())
            .ToList();

        List<PendingPlan> plans = [];
        foreach (var group in groups) {
            var container = group.Key.Container;
            if (container is null) {
                continue;
            }

            var refs = group.OrderBy(id => id.SpanStart).ToList();
            if (!TryGetFirstAssignment(refs, out var left, out var assignment, out var assignmentStatement)) {
                continue;
            }

            if (refs.Any(r => r.SpanStart < left.SpanStart)) {
                continue;
            }

            if (HasTypeMemberNamed(container, group.Key.Name)) {
                continue;
            }

            var inferredType = InferType(assignment.Right);
            var annotation = new SyntaxAnnotation();
            plans.Add(new PendingPlan(annotation, group.Key.Name, inferredType, assignmentStatement.SpanStart));
        }

        if (plans.Count == 0) {
            return (root, plans);
        }

        var anchorsByOrder = plans
            .GroupBy(p => p.Order)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Anchor).ToArray());

        var anchorStatements = root
            .DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Where(s => anchorsByOrder.ContainsKey(s.SpanStart))
            .ToList();

        if (anchorStatements.Count > 0) {
            root = root.ReplaceNodes(anchorStatements,
                (original, rewritten) => rewritten.WithAdditionalAnnotations(anchorsByOrder[original.SpanStart]));
        }

        return (root, plans);
    }

    private CompilationUnitSyntax ApplyPlans(CompilationUnitSyntax root, List<PendingPlan> plans)
    {
        SyntaxNode updated = root;

        foreach (var plan in plans.OrderByDescending(p => p.Order)) {
            var currentAnchor = updated.GetAnnotatedNodes(plan.Anchor).OfType<ExpressionStatementSyntax>().FirstOrDefault();
            if (currentAnchor?.Parent is not BlockSyntax block) {
                continue;
            }

            if (HasLocalDeclarationBefore(block, currentAnchor, plan.Name)) {
                continue;
            }

            var declaration = LocalDeclarationStatement(
                VariableDeclaration(
                    plan.Type,
                    SingletonSeparatedList(
                        VariableDeclarator(Identifier(plan.Name))
                            .WithInitializer(EqualsValueClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression))))));

            int insertIndex = block.Statements.IndexOf(currentAnchor);
            if (insertIndex < 0) {
                continue;
            }

            var nextBlock = block.WithStatements(block.Statements.Insert(insertIndex, declaration));
            updated = updated.ReplaceNode(block, nextBlock);
        }

        return (CompilationUnitSyntax)updated;
    }

    private bool ShouldConsiderIdentifier(IdentifierNameSyntax id)
    {
        if (id.Parent is MemberAccessExpressionSyntax memberAccess && ReferenceEquals(memberAccess.Name, id)) {
            return false;
        }

        if (id.Parent is VariableDeclarationSyntax or ParameterSyntax or TypeArgumentListSyntax) {
            return false;
        }

        var info = semantics.GetSymbolInfo(id);
        if (info.Symbol is not null) {
            return false;
        }

        return true;
    }

    private static SyntaxNode GetReferenceContainer(SyntaxNode node)
        => node?.AncestorsAndSelf().FirstOrDefault(n
            => n is AccessorDeclarationSyntax
                or BaseMethodDeclarationSyntax
                or GlobalStatementSyntax
                or MemberDeclarationSyntax);

    private static bool TryGetFirstAssignment(
        List<IdentifierNameSyntax> refs,
        out IdentifierNameSyntax left,
        out AssignmentExpressionSyntax assignment,
        out ExpressionStatementSyntax statement)
    {
        left = null;
        assignment = null;
        statement = null;

        foreach (var id in refs) {
            if (id.Parent is not AssignmentExpressionSyntax assign
                || !assign.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || !ReferenceEquals(assign.Left, id)
                || assign.Parent is not ExpressionStatementSyntax expressionStatement
                || expressionStatement.Parent is not BlockSyntax) {
                continue;
            }

            left = id;
            assignment = assign;
            statement = expressionStatement;
            return true;
        }

        return false;
    }

    private bool HasTypeMemberNamed(SyntaxNode container, string name)
    {
        var typeDecl = container.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) {
            return false;
        }

        if (semantics.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSymbol) {
            return false;
        }

        for (INamedTypeSymbol current = typeSymbol; current is not null; current = current.BaseType) {
            if (current.GetMembers().Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        }

        return false;
    }

    private TypeSyntax InferType(ExpressionSyntax expression)
    {
        var rhsType = semantics.GetTypeInfo(expression).Type;
        if (rhsType is null || rhsType.TypeKind == TypeKind.Error) {
            return IdentifierName("dynamic");
        }

        return rhsType.ToTypeSyntax();
    }

    private static bool HasLocalDeclarationBefore(BlockSyntax block, StatementSyntax anchor, string name)
    {
        int anchorIndex = block.Statements.IndexOf(anchor);
        if (anchorIndex <= 0) {
            return false;
        }

        for (int i = 0; i < anchorIndex; i++) {
            if (block.Statements[i] is not LocalDeclarationStatementSyntax localDecl) {
                continue;
            }

            if (localDecl.Declaration.Variables.Any(v => string.Equals(v.Identifier.ValueText, name, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        }

        return false;
    }

    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>
    {
        public bool Equals(GroupKey x, GroupKey y)
        {
            if (x is null || y is null) {
                return false;
            }

            return ReferenceEquals(x.Container, y.Container)
                   && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(GroupKey obj)
            => HashCode.Combine(obj.Container, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}