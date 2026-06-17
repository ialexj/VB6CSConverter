using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Refines array declarations based on their initializers.
/// Given:
///   string[] x = default;
///   x = new dynamic[0, 0];
/// Produces:
///   string[,] x = default;
///   x = new string[0, 0];
///
/// The rewrite only happens when all array creation expressions for a given
/// variable agree on rank and element types are compatible (no conflicting
/// specific types).
/// </summary>
public class ArrayRefinementRewriter(
    SemanticModel semantics,
    Dictionary<VariableDeclaratorSyntax, ArrayTypeSyntax> declaratorTypes,
    Dictionary<ISymbol, ArrayTypeSyntax> symbolTypes) : LoggedRewriter
{
    public static async Task GetAllArrayVariablesAndUsages(
        SemanticModel semantics,
        Solution solution,
        Dictionary<VariableDeclaratorSyntax, ArrayTypeSyntax> declaratorTypes,
        Dictionary<ISymbol, ArrayTypeSyntax> symbolTypes)
    {
        var declarators = semantics.SyntaxTree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(d => d.Parent is VariableDeclarationSyntax { Type: ArrayTypeSyntax });

        foreach (var declarator in declarators) {
            var symbol = semantics.GetDeclaredSymbol(declarator);
            if (symbol is not ILocalSymbol and not IFieldSymbol)
                continue;

            if (symbol is ILocalSymbol { Type: not IArrayTypeSymbol }
                or IFieldSymbol { Type: not IArrayTypeSymbol })
                continue;

            var declaredArrayType = (ArrayTypeSyntax)((VariableDeclarationSyntax)declarator.Parent!).Type;

            var arrayCreations = new List<ArrayCreationExpressionSyntax>();
            bool skip = false;

            // Collect from the declaration initializer (in the current file)
            if (declarator.Initializer?.Value is ArrayCreationExpressionSyntax initCreation)
                arrayCreations.Add(initCreation);

            // Find all assignment references across the solution
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
            foreach (var reference in references) {
                foreach (var location in reference.Locations) {
                    var sem = await location.Document.GetSemanticModelAsync();
                    var node = sem!.SyntaxTree.GetCompilationUnitRoot()
                        .FindNode(location.Location.SourceSpan);

                    if (node.Parent is AssignmentExpressionSyntax assignment
                        && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                        && assignment.Left.Contains(node)) {

                        if (assignment.Right is ArrayCreationExpressionSyntax creation) {
                            arrayCreations.Add(creation);
                        }
                        else if (assignment.Right is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression }) {
                            // transparent — default literal does not constrain the type
                        }
                        else {
                            skip = true;
                            break;
                        }
                    }
                }
                if (skip) break;
            }

            if (skip || arrayCreations.Count == 0)
                continue;

            // Verify all array creations agree on rank
            int? agreedRank = null;
            foreach (var creation in arrayCreations) {
                int rank = creation.Type.RankSpecifiers[0].Sizes.Count;
                if (agreedRank is null)
                    agreedRank = rank;
                else if (agreedRank != rank) {
                    skip = true;
                    break;
                }
            }
            if (skip) continue;

            // Determine the best (most specific) element type
            // Gather element types syntactically — no additional SemanticModel needed
            var declaredElementType = declaredArrayType.ElementType;
            var allElementTypes = arrayCreations
                .Select(c => c.Type.ElementType)
                .Prepend(declaredElementType)
                .ToList();

            var specificTypes = allElementTypes.Where(IsSpecificType).ToList();

            TypeSyntax bestElementType;
            bool typeNeedsChange;

            if (specificTypes.Count == 0) {
                // All element types are dynamic/object — only a rank change is possible.
                bestElementType = declaredElementType;
                typeNeedsChange = false;
            }
            else {
                var firstSpecificText = specificTypes[0].ToString();
                if (specificTypes.Any(t => t.ToString() != firstSpecificText))
                    continue; // conflicting specific types — unsafe to refine

                bestElementType = specificTypes[0];
                // A type change is needed if ANY of the element types (declared or in a creation)
                // differs from the best specific type — e.g. declaration is string[] but a
                // creation is new dynamic[…], or declaration is dynamic[] but a creation is new string[…].
                typeNeedsChange = allElementTypes.Any(t => t.ToString() != firstSpecificText);
            }

            int currentRank = declaredArrayType.RankSpecifiers[0].Sizes.Count;
            bool rankNeedsChange = currentRank != agreedRank!.Value;

            if (!rankNeedsChange && !typeNeedsChange)
                continue;

            var newArrayType = BuildArrayType(bestElementType, agreedRank.Value);
            declaratorTypes[declarator] = newArrayType;
            symbolTypes[symbol] = newArrayType;
        }
    }

    // ── Rewriter ─────────────────────────────────────────────────────────────

    public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        // Let children be rewritten first (handles initializer array creations),
        // then update the declared array type.
        var visited = (VariableDeclarationSyntax)base.VisitVariableDeclaration(node)!;

        var declarator = node.Variables.First();
        if (declaratorTypes.TryGetValue(declarator, out var newArrayType)) {
            return Rewrite(node, _ => visited.WithType(newArrayType));
        }

        return visited;
    }

    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        if (node.Initializer?.Value is ArrayCreationExpressionSyntax creation) {
            var symbol = semantics?.GetDeclaredSymbol(node);
            if (symbol != null && symbolTypes.TryGetValue(symbol, out var newArrayType)) {
                return Rewrite(node, n =>
                    n.WithInitializer(n.Initializer!.WithValue(
                        ((ArrayCreationExpressionSyntax)n.Initializer.Value)
                            .WithType(creation.Type.WithElementType(newArrayType.ElementType)))));
            }
        }

        return base.VisitVariableDeclarator(node);
    }

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && node.Right is ArrayCreationExpressionSyntax creation
            && semantics != null) {

            var symbol = semantics.GetSymbolInfo(node.Left).Symbol;
            if (symbol != null && symbolTypes.TryGetValue(symbol, out var newArrayType)) {
                return Rewrite(node, n =>
                    n.WithRight(
                        ((ArrayCreationExpressionSyntax)n.Right)
                            .WithType(creation.Type.WithElementType(newArrayType.ElementType))));
            }
        }

        return base.VisitAssignmentExpression(node);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static bool IsSpecificType(TypeSyntax type) =>
        type is not IdentifierNameSyntax { Identifier.Text: "dynamic" }
        && type is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.ObjectKeyword };

    static ArrayTypeSyntax BuildArrayType(TypeSyntax elementType, int rank)
    {
        var omittedSizes = Enumerable.Range(0, rank)
            .Select(_ => (SyntaxNodeOrToken)OmittedArraySizeExpression())
            .Intersperse(Token(SyntaxKind.CommaToken))
            .ToArray();

        return ArrayType(
            elementType,
            SingletonList(
                ArrayRankSpecifier(
                    SeparatedList<ExpressionSyntax>(omittedSizes))));
    }
}
