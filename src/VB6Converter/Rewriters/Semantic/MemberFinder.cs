using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace VB6Converter.Rewriters.Semantic;

public class MemberFinder(SemanticModel sem) : LoggedRewriter
{
    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Log.Rewrite(this, node, node => {
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
        => Log.Rewrite(this, node, node => {
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
            if (param is not null && param.Name != passedName)
                return base.VisitArgument(node.WithNameColon(node.NameColon.WithName(SyntaxFactory.IdentifierName(param.Name))));

            return base.VisitArgument(node);
        });
}
