using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

/// <summary>
/// Prefixes parameter names and variable declarators that collide with C# reserved
/// keywords with <c>@</c> (e.g. <c>default</c> → <c>@default</c>).
/// </summary>
public class KeywordEscapeRewriter(string file = null) : LoggedRewriter(file)
{
    static bool IsKeyword(string text)
        => SyntaxFacts.GetKeywordKind(text) != SyntaxKind.None;

    static SyntaxToken Escape(SyntaxToken token)
        => Identifier("@" + token.ValueText).WithTriviaFrom(token);

    public override SyntaxNode VisitParameter(ParameterSyntax node)
        => Rewrite(node, node => {
            if (!node.Identifier.IsVerbatimIdentifier() && IsKeyword(node.Identifier.ValueText)) {
                return (SyntaxNode)base.VisitParameter(node.WithIdentifier(Escape(node.Identifier)));
            }
            return base.VisitParameter(node);
        });

    public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        => Rewrite(node, node => {
            if (!node.Identifier.IsVerbatimIdentifier() && IsKeyword(node.Identifier.ValueText)) {
                return (SyntaxNode)base.VisitVariableDeclarator(node.WithIdentifier(Escape(node.Identifier)));
            }
            return base.VisitVariableDeclarator(node);
        });

    public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        => Rewrite(node, node => {
            if (!node.Identifier.IsVerbatimIdentifier() && IsKeyword(node.Identifier.ValueText)) {
                node = node.WithIdentifier(Escape(node.Identifier));
            }
            return base.VisitForEachStatement(node);
        });

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Rewrite(node, node => {
            if (node.Parent is QualifiedNameSyntax
                    || node.Parent is FileScopedNamespaceDeclarationSyntax
                    || node.Parent is NamespaceDeclarationSyntax
                    || node.Parent is UsingDirectiveSyntax
                    || node.Parent is AliasQualifiedNameSyntax) {
                return base.VisitIdentifierName(node);
            }

            // `this` may be represented as an IdentifierName in generated trees;
            // never rewrite it to `@this`.
            if (node.Identifier.ValueText == "this") {
                return base.VisitIdentifierName(node);
            }

            if (!node.Identifier.IsVerbatimIdentifier() && IsKeyword(node.Identifier.ValueText)) {
                return base.VisitIdentifierName(node.WithIdentifier(Escape(node.Identifier)));
            }

            return base.VisitIdentifierName(node);
        });
}
