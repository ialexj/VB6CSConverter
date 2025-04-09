using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.CSharpModel
{
    internal static class RoslynHelpers
    {

        public static ArgumentListSyntax ArgumentList(params ExpressionSyntax[] args)
        {
            if (args is null || args.Length == 0) {
                return SyntaxFactory.ArgumentList();
            }
            else if (args.Length == 1) {
                return SyntaxFactory.ArgumentList(SingletonSeparatedList(Argument(args[0])));
            }
            else {
                return SyntaxFactory.ArgumentList(SeparatedList<ArgumentSyntax>(
                    new SyntaxNodeOrTokenList(args
                        .Select(a => (SyntaxNodeOrToken)Argument(a))
                        .Intersperse(Token(SyntaxKind.CommaToken)))));
            }
        }

        public static ArgumentListSyntax ArgumentList(params ArgumentSyntax[] args)
        {
            if (args is null || args.Length == 0) {
                return SyntaxFactory.ArgumentList();
            }
            else if (args.Length == 1) {
                return SyntaxFactory.ArgumentList(SingletonSeparatedList(args[0]));
            }
            else {
                return SyntaxFactory.ArgumentList(SeparatedList<ArgumentSyntax>(
                    new SyntaxNodeOrTokenList(args
                        .Select(a => (SyntaxNodeOrToken)a)
                        .Intersperse(Token(SyntaxKind.CommaToken)))));
            }
        }
    }
}