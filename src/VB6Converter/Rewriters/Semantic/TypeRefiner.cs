using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Rewriters.Semantic;
public class TypeRefiner(ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax> varTypes) : LoggedRewriter
{
    public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        var declarator = node.Variables.First();

        if (varTypes.TryGetValue(declarator, out var typeSymbol)) {
            return node.WithType(typeSymbol.WithTriviaFrom(node.Type));
        }

        return base.VisitVariableDeclaration(node);
    }


    public static async Task GetAllVariablesAndUsages(ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax> vars, SemanticModel semantics, Solution solution)
    {
        var variables = semantics.SyntaxTree.GetCompilationUnitRoot()
            .DescendantNodes(d => d is not VariableDeclaratorSyntax)
            .OfType<VariableDeclaratorSyntax>();

        foreach (var variable in variables) {
            var symbol = semantics.GetDeclaredSymbol(variable);
            if (symbol != null) {
                ITypeSymbol type;
                if (symbol is ILocalSymbol local) {
                    type = local.Type;
                }
                else if (symbol is IFieldSymbol field) {
                    type = field.Type;
                }
                else {
                    continue;
                }

                if (type.SpecialType != SpecialType.System_Object && type.TypeKind != TypeKind.Dynamic) {
                    continue;
                }

                // Initializer-only declarations (e.g. const fields) never appear as assignment expressions.
                // Infer from the declaration initializer so these members can be refined.
                if (variable.Initializer is { Value: var initExpr }) {
                    var initType = semantics.GetTypeInfo(initExpr).Type;
                    if (IsUsableRefinementType(initType, type)) {
                        var typeSyntax = initType.ToTypeSyntax();

                        vars.AddOrUpdate(variable, typeSyntax, (_, existing) => {
                            if (Equals(typeSyntax.ToString(), existing.ToString())) {
                                return existing;
                            }

                            return type.ToTypeSyntax(); // keep as is
                        });
                    }
                }

                var references = await SymbolFinder.FindReferencesAsync(symbol, solution);

                foreach (var reference in references) {
                    foreach (var location in reference.Locations) {
                        var sem = await location.Document.GetSemanticModelAsync();
                        var node = sem.SyntaxTree.GetCompilationUnitRoot()
                            .FindNode(location.Location.SourceSpan);

                        if (node.Parent is AssignmentExpressionSyntax assignment) {
                            var rightType = sem.GetTypeInfo(assignment.Right).Type;
                            if (IsUsableRefinementType(rightType, type)) {

                                var typeSyntax = rightType.ToTypeSyntax();

                                vars.AddOrUpdate(variable, typeSyntax, (variable, existing) => {
                                    if (Equals(typeSyntax.ToString(), existing.ToString())) {
                                        return existing;
                                    }

                                    return type.ToTypeSyntax(); // keep as is
                                });
                            }
                        }
                    }
                }

            }
        }
    }

    private static bool IsUsableRefinementType(ITypeSymbol candidate, ITypeSymbol existing)
        => candidate != null
            && candidate.SpecialType != SpecialType.System_Object
            && candidate.TypeKind != TypeKind.Dynamic
            && candidate.TypeKind != TypeKind.Error
            && candidate.TypeKind != TypeKind.Array
            && candidate.ToString() != "Microsoft.VisualBasic.VariantType"
            && !SymbolEqualityComparer.Default.Equals(existing, candidate);
}
