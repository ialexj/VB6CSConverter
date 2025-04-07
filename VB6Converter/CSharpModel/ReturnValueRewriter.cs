using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.CSharpModel;

public class ReturnValueRewriter(IdentifierNameSyntax identifier) : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
    {
        return ReturnStatement(identifier).WithTriviaFrom(node);
    }

    public static BlockSyntax RewriteMethodReturn(TypeSyntax methodType, string methodName, BlockSyntax body)
    {
        if (methodType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)) {
            return body;
        }

        var block = Block()
            .AddStatements(
                LocalDeclarationStatement(
                    VariableDeclaration(methodType, SingletonSeparatedList(
                        VariableDeclarator(methodName)
                            .WithInitializer(
                                EqualsValueClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression))
                            )))));

        var returnIdentifier = IdentifierName(methodName);

        var rewriter = new ReturnValueRewriter(returnIdentifier);

        foreach (var statement in body.Statements) {
            var newStatement = (StatementSyntax)rewriter.Visit(statement);
            block = block.AddStatements(newStatement);
        }

        if (block.Statements[^1] is not ReturnStatementSyntax) {
            block = block.AddStatements(ReturnStatement(returnIdentifier));
        }

        return block;
    }
}
