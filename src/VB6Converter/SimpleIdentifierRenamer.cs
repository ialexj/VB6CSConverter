using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter;

class SimpleIdentifierRenamer(string from, string to) : LoggedRewriter
{
    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (Equals(node.Identifier.Value, from) && !IsMemberAccessName(node)) {
            return IdentifierName(to).WithTriviaFrom(node);
        }
        else {
            return base.VisitIdentifierName(node);
        }
    }

    static bool IsMemberAccessName(IdentifierNameSyntax node)
    {
        return node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node
            || node.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node;
    }
}
