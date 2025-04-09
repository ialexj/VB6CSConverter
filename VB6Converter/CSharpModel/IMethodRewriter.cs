using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VB6Converter.CSharpModel;

public interface IMethodRewriter
{
    ExpressionSyntax RewriteIdentifier(ExpressionSyntax identifier);

    ArgumentListSyntax RewriteArguments(ArgumentListSyntax arguments, ExpressionSyntax identifier);
}
