using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace VB6Converter.Rewriters.Semantic;

// VB6 "Property Get"/functions without an explicit "As" clause (or "As Variant")
// are emitted with a "dynamic"/"object" return type. When such a property is
// get-only (a computed property with no setter), its real return type can
// usually be determined directly from the getter's own expression - upgrade it
// so downstream rewriters (e.g. BitwiseOrRewriter) see a concrete, non-dynamic
// type instead of guessing.
public class PropertyTypeRefiner(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        => Rewrite(node, node =>
        {
            if (!IsDynamicOrObject(node.Type))
                return base.VisitPropertyDeclaration(node);

            var accessors = node.AccessorList?.Accessors;
            if (accessors is null
                || accessors.Value.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))) {
                // A setter's parameter type would also need to agree; don't attempt that here.
                return base.VisitPropertyDeclaration(node);
            }

            var getter = accessors.Value.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter is null)
                return base.VisitPropertyDeclaration(node);

            var returnType = GetReturnType(getter);
            if (!IsUsableRefinementType(returnType))
                return base.VisitPropertyDeclaration(node);

            return node.WithType(returnType.ToTypeSyntax().WithTriviaFrom(node.Type));
        });

    private ITypeSymbol GetReturnType(AccessorDeclarationSyntax getter)
    {
        if (getter.ExpressionBody is { } arrow)
            return semantics.GetTypeInfo(arrow.Expression).Type;

        // Return statements may be nested inside if/switch/loop blocks, not just at the
        // top level - walk the whole body, but don't cross into nested function boundaries.
        var returns = getter.Body?
            .DescendantNodes(n => n is not AnonymousFunctionExpressionSyntax && n is not LocalFunctionStatementSyntax)
            .OfType<ReturnStatementSyntax>()
            .ToArray();
        if (returns is not { Length: > 0 })
            return null;

        ITypeSymbol result = null;
        foreach (var ret in returns) {
            if (ret.Expression is null)
                return null;

            var type = semantics.GetTypeInfo(ret.Expression).Type;
            if (type is null)
                return null;

            if (result is null) {
                result = type;
            }
            else if (!SymbolEqualityComparer.Default.Equals(result, type)) {
                return null; // Ambiguous - different return statements disagree on type.
            }
        }

        return result;
    }

    private static bool IsDynamicOrObject(TypeSyntax type)
        => type is IdentifierNameSyntax { Identifier.Text: "dynamic" }
           || type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.ObjectKeyword);

    private static bool IsUsableRefinementType(ITypeSymbol type)
        => type is not null
           && type.SpecialType != SpecialType.System_Object
           && type.TypeKind != TypeKind.Dynamic
           && type.TypeKind != TypeKind.Error
           && type.TypeKind != TypeKind.Array
           && type.ToString() != "Microsoft.VisualBasic.VariantType";
}
