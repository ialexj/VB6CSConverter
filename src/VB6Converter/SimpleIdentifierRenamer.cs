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
        if (Equals(node.Identifier.Value, from)) {
            return IdentifierName(to).WithTriviaFrom(node);
        }
        else {
            return base.VisitIdentifierName(node);
        }
    }
}
