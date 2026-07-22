using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

public class ConstFieldRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        => Rewrite(node, node => {
            if (!node.Modifiers.Any(SyntaxKind.ConstKeyword)) {
                return node;
            }

            if (node.Declaration.Variables.Any(v => HasNonConstantInitializer(v))) {
                return node.WithModifiers(GetStaticReadonlyModifiers(node.Modifiers));
            }

            return node;
        });

    private bool HasNonConstantInitializer(VariableDeclaratorSyntax variable)
    {
        if (variable.Initializer is null) {
            return false;
        }

        var constantValue = semantics.GetConstantValue(variable.Initializer.Value);
        return !constantValue.HasValue;
    }

    private static SyntaxTokenList GetStaticReadonlyModifiers(SyntaxTokenList modifiers)
    {
        var visibility = modifiers.FirstOrDefault(t =>
            t.IsKind(SyntaxKind.PublicKeyword)
            || t.IsKind(SyntaxKind.PrivateKeyword)
            || t.IsKind(SyntaxKind.ProtectedKeyword)
            || t.IsKind(SyntaxKind.InternalKeyword)
            || t.IsKind(SyntaxKind.FileKeyword));

        var extraTokens = modifiers
            .Where(t => !t.IsKind(SyntaxKind.ConstKeyword) && !t.IsKind(SyntaxKind.PublicKeyword) && !t.IsKind(SyntaxKind.PrivateKeyword) && !t.IsKind(SyntaxKind.ProtectedKeyword) && !t.IsKind(SyntaxKind.InternalKeyword) && !t.IsKind(SyntaxKind.FileKeyword))
            .ToList();

        var tokens = new List<SyntaxToken>();
        if (visibility != default) {
            tokens.Add(visibility.WithTrailingTrivia(Space));
        }

        tokens.Add(Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(Space));
        tokens.Add(Token(SyntaxKind.ReadOnlyKeyword).WithTrailingTrivia(Space));
        tokens.AddRange(extraTokens);
        return TokenList(tokens);
    }
}
