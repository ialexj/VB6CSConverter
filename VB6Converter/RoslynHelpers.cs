using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections;
using System.Linq;
using System.Xml.Linq;
using VB6Converter.Conversion;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter
{
    internal static class RoslynHelpers
    {
        public static NameSyntax ToName(this ExpressionSyntax expr)
        {
            if (expr is NameSyntax name) {
                return name;
            }
            else if (expr is MemberAccessExpressionSyntax member) {
                var obj = ToName(member.Expression);
                var target = member.Name;
                return QualifiedName(obj, target);
            }
            else {
                throw new ArgumentException("Expression is not a name");
            }
        }

        public static NameSyntax AppendName(this ExpressionSyntax left, ExpressionSyntax right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);

            var leftName = left.ToName();
            var rightName = right.ToName();

            var current = leftName;
            foreach (var id in right.DescendantNodesAndSelf().OfType<SimpleNameSyntax>()) {
                current = QualifiedName(current, id);
            }

            return current;
        }

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

        public static StatementSyntax GetBlock(StatementSyntax[] statements, bool allowCollapse)
        {
            if (allowCollapse) {
                if (statements is null || statements.Length == 0) {
                    return EmptyStatement();
                }
                else if (statements.Length == 1) {
                    return statements[0];
                }
                else {
                    return Block(statements);
                }
            }
            else {
                return Block(statements ?? []);
            }
        }
    }
}