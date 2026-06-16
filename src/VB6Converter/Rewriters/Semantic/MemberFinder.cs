using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Attempts to find the correct casing for member accesses and named arguments based on the semantic model.
/// </summary>
/// <param name="sem">The semantic model used to retrieve type and symbol information.</param>
public class MemberFinder(SemanticModel sem) : LoggedRewriter
{
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
