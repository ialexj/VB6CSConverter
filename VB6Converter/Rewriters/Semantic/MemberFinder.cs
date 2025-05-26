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
}
