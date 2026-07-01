using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Attempts to find the correct casing for member accesses and named arguments based on the semantic model.
/// </summary>
/// <param name="sem">The semantic model used to retrieve type and symbol information.</param>
public class SymbolCapitalizationRewriter(SemanticModel sem) : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (!IsTypeUsage(node, sem)) {
                return base.VisitIdentifierName(node);
            }

            if (sem.GetSymbolInfo(node).Symbol is ITypeSymbol) {
                return base.VisitIdentifierName(node);
            }

            var type = RoslynHelpers.FindTypeByName(sem, node.Identifier.Text);
            if (type != null && !string.Equals(type.ToString(), node.Identifier.Text, StringComparison.Ordinal)) {
                return SyntaxFactory.ParseName(type.ToString()).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        });

    /// <summary>
    /// Determines whether <paramref name="node"/> sits in a syntactic position where it is
    /// expected to name a type (as opposed to a variable, method, or member name). VB6
    /// identifiers are case-insensitive, so a class/form may be declared with one casing (e.g.
    /// "frmclientesmain") and referenced elsewhere with another (e.g. "frmClientesMain");
    /// this lets those references be corrected to the declared casing.
    /// </summary>
    static bool IsTypeUsage(IdentifierNameSyntax node, SemanticModel sem)
        => node.Parent switch {
            ObjectCreationExpressionSyntax oce => oce.Type == node,
            ArrayTypeSyntax at => at.ElementType == node,
            BaseTypeSyntax bts => bts.Type == node,
            CastExpressionSyntax ce => ce.Type == node,
            MethodDeclarationSyntax mds => mds.ReturnType == node,
            PropertyDeclarationSyntax pds => pds.Type == node,
            IndexerDeclarationSyntax ids => ids.Type == node,
            // Bare identifier used as a static-member qualifier (e.g. frmClientesMain.SharedField)
            // that doesn't resolve to anything at all - a strong signal of a mis-cased type name
            // rather than an ordinary unresolved variable/method reference.
            MemberAccessExpressionSyntax maes when maes.Expression == node
                => sem.GetSymbolInfo(node) is { Symbol: null, CandidateSymbols.IsEmpty: true },
            _ => false,
        };

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Rewrite(node, node => {
            var type = sem.GetTypeInfo(node.Expression).ConvertedType;
            if (type is null
                || type.Name == string.Empty
                || type.Name == "var"
                || type.SpecialType != SpecialType.None
                || type.ToString().StartsWith("System")) {
                return base.VisitMemberAccessExpression(node);
            }

            string name = node.Name.ToString();
            var member = type.GetMembers(name).FirstOrDefault();
            if (member is null) {
                member = type.GetBaseTypesAndThis().SelectMany(m => m.GetMembers()).FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (member is not null) {
                    return node.WithName(SyntaxFactory.IdentifierName(member.Name));
                }
            }

            // Handle pseudo-properties backed by GetX/SetX method pairs (e.g. GetValue/SetValue).
            // VB6 code may spell the property with wrong casing (e.g. obj.value). No direct or
            // case-insensitive member is found above, so look for a "Get" + name method.
            // - Getter call site (InvocationExpressionSyntax parent): rename to GetX so the call
            //   resolves directly.
            // - Setter element-access site (ElementAccessExpressionSyntax parent): rename to the
            //   canonical property name (strip the "Get" prefix) so that ParameterizedPropertyRewriter
            //   can later find SetX via "Set" + canonicalName.
            if (member is null) {
                string getterCandidate = "Get" + name;
                var getter = type.GetBaseTypesAndThis()
                    .SelectMany(t => t.GetMembers())
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => string.Equals(m.Name, getterCandidate, StringComparison.OrdinalIgnoreCase));

                if (getter is not null) {
                    string newName = node.Parent is InvocationExpressionSyntax
                        ? getter.Name               // obj.value(k)  → obj.GetValue(k)
                        : getter.Name.Substring(3); // obj.value[k]  → obj.Value[k]  (for SetValue later)
                    return node.WithName(SyntaxFactory.IdentifierName(newName));
                }
            }

            return node; // don't recurse
        });

    public override SyntaxNode VisitArgument(ArgumentSyntax node)
        => Rewrite(node, node => {
            if (node.NameColon is null)
                return base.VisitArgument(node);

            if (node.Parent is not ArgumentListSyntax list
                || list.Parent is not InvocationExpressionSyntax invocation)
                return base.VisitArgument(node);

            var symbolInfo = sem.GetSymbolInfo(invocation);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method)
                return base.VisitArgument(node);

            var passedName = node.NameColon.Name.Identifier.Text;
            var param = method.Parameters.FirstOrDefault(p => string.Equals(p.Name, passedName, StringComparison.OrdinalIgnoreCase));
            if (param is not null && param.Name != passedName) {
                // Visit children on the original node first to keep them within the semantic model's
                // syntax tree. Applying the name-colon change to a pre-visited floating node causes
                // "Syntax node is not within syntax tree" when the rewriter later calls GetTypeInfo
                // on the adopted children.
                var visited = (ArgumentSyntax)base.VisitArgument(node);
                return visited.WithNameColon(visited.NameColon!.WithName(SyntaxFactory.IdentifierName(param.Name)));
            }

            return base.VisitArgument(node);
        });
}
