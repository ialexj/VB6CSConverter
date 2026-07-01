using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.Rewriters;

/// <summary>
/// Fixes up VB6-style "For Each" loops: the loop variable is declared separately
/// (e.g. via a "Dim" that became "Type x = default;") in an outer scope, possibly
/// several blocks above the "foreach" itself (e.g. when the loop is nested inside
/// a collapsed "With"/"If" block). This rewriter finds that declaration, moves its
/// type onto the "foreach" (unless it's "object"/"dynamic", in which case "var" is
/// kept so the type is inferred from the collection), and removes the now-redundant
/// declaration - but only when it isn't referenced anywhere outside the loop itself.
/// </summary>
public class ForEachVariableRewriter(SemanticModel semantics) : LoggedRewriter
{
    private sealed record Plan(
        ForEachStatementSyntax ForEach,
        TypeSyntax NewType,
        LocalDeclarationStatementSyntax Declaration,
        VariableDeclaratorSyntax Declarator);

    public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
        => Rewrite(node, node => {
            if (!ReferenceEquals(node.SyntaxTree, semantics.SyntaxTree)) {
                return node;
            }

            var plans = CollectPlans(node);
            return plans.Count == 0 ? node : Apply(node, plans);
        });

    private List<Plan> CollectPlans(CompilationUnitSyntax root)
    {
        var foreaches = root
            .DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<ForEachStatementSyntax>();

        var candidates = new Dictionary<ForEachStatementSyntax, (LocalDeclarationStatementSyntax Declaration, VariableDeclaratorSyntax Declarator)>();

        foreach (var fe in foreaches) {
            if (FindEnclosingDeclarator(fe, fe.Identifier.Text) is { } found) {
                candidates[fe] = found;
            }
        }

        // If more than one "For Each" would claim the same declaration, leave them
        // all alone rather than risk removing a declaration still needed elsewhere.
        var claimedByMultiple = candidates.Values
            .GroupBy(c => c.Declarator)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var plans = new List<Plan>();

        foreach (var (fe, candidate) in candidates) {
            if (claimedByMultiple.Contains(candidate.Declarator)) {
                continue;
            }

            if (!IsSafeToRemove(candidate.Declarator, fe)) {
                continue;
            }

            var declaredType = ((VariableDeclarationSyntax)candidate.Declarator.Parent).Type;
            var newType = IsObjectOrDynamicType(declaredType) ? null : declaredType;

            plans.Add(new Plan(fe, newType, candidate.Declaration, candidate.Declarator));
        }

        return plans;
    }

    private static SyntaxNode Apply(CompilationUnitSyntax root, List<Plan> plans)
    {
        var trackedNodes = plans
            .SelectMany(p => new SyntaxNode[] { p.ForEach, p.Declaration, p.Declarator })
            .Distinct();

        var tracked = root.TrackNodes(trackedNodes);

        foreach (var plan in plans) {
            if (plan.NewType is not null && tracked.GetCurrentNode(plan.ForEach) is ForEachStatementSyntax currentForEach) {
                tracked = tracked.ReplaceNode(currentForEach, currentForEach.WithType(plan.NewType));
            }

            if (tracked.GetCurrentNode(plan.Declaration) is not LocalDeclarationStatementSyntax currentDeclaration) {
                continue;
            }

            if (currentDeclaration.Declaration.Variables.Count == 1) {
                tracked = tracked.RemoveNode(currentDeclaration, SyntaxRemoveOptions.KeepNoTrivia);
            }
            else if (tracked.GetCurrentNode(plan.Declarator) is VariableDeclaratorSyntax currentDeclarator) {
                tracked = tracked.ReplaceNode(currentDeclaration, currentDeclaration.WithDeclaration(
                    currentDeclaration.Declaration.RemoveNode(currentDeclarator, SyntaxRemoveOptions.KeepNoTrivia)));
            }
        }

        return tracked;
    }

    // Finds the nearest enclosing "Dim"-turned-declaration matching the loop variable's
    // name, walking up through ancestor blocks (e.g. out of a collapsed "With"/"If"
    // block) but never past the containing method/accessor.
    private static (LocalDeclarationStatementSyntax Declaration, VariableDeclaratorSyntax Declarator)? FindEnclosingDeclarator(
        ForEachStatementSyntax fe, string name)
    {
        foreach (var block in GetEnclosingBlocks(fe)) {
            if (FindDeclarator(block, name) is VariableDeclaratorSyntax declarator) {
                return ((LocalDeclarationStatementSyntax)declarator.Parent.Parent, declarator);
            }
        }

        return null;
    }

    private static IEnumerable<BlockSyntax> GetEnclosingBlocks(ForEachStatementSyntax fe)
    {
        foreach (var block in fe.AncestorsAndSelf().OfType<BlockSyntax>()) {
            yield return block;

            if (block.Parent is AccessorDeclarationSyntax or BaseMethodDeclarationSyntax or GlobalStatementSyntax) {
                yield break;
            }
        }
    }

    static VariableDeclaratorSyntax FindDeclarator(BlockSyntax block, string name)
        => block.ChildNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(d => d.Declaration.Variables)
            .FirstOrDefault(v => v.Identifier.Text.Equals(name));

    private static bool IsObjectOrDynamicType(TypeSyntax type)
        => type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.ObjectKeyword)
           || type is IdentifierNameSyntax identifier && identifier.Identifier.Text == "dynamic";

    // Safe to remove only when every reference to the declared local falls inside the
    // "For Each" statement itself (i.e. the only use is the loop we're rewriting).
    private bool IsSafeToRemove(VariableDeclaratorSyntax declarator, ForEachStatementSyntax fe)
    {
        if (semantics.GetDeclaredSymbol(declarator) is not ILocalSymbol symbol) {
            return false;
        }

        var container = GetReferenceContainer(declarator);

        var references = semantics.SyntaxTree.GetRoot()
            .DescendantNodes(descendIntoChildren: n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<IdentifierNameSyntax>()
            .Where(id => id.Identifier.ValueText == symbol.Name)
            .Where(id => IsSameContainer(container, GetReferenceContainer(id)))
            .Where(id => {
                var resolved = semantics.GetSymbolInfo(id).Symbol;
                return resolved is null || SymbolEqualityComparer.Default.Equals(resolved, symbol);
            });

        return references.All(id => fe.Span.Contains(id.Span));
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
}
