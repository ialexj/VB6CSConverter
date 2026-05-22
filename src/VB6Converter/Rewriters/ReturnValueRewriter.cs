using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog.Parsing;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class ReturnValueRewriter(SyntaxToken? valueName = null) : LoggedRewriter
{
    public static readonly ReturnValueRewriter Default = new();

    public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        => Rewrite(node, node => {
            var propName = valueName ?? Identifier("Item");
            var accessors = node.AccessorList;

            if (accessors.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) is not AccessorDeclarationSyntax get) {
                return base.VisitIndexerDeclaration(node);
            }
            if (get.Body is null) {
                return base.VisitIndexerDeclaration(node);
            }

            var newGet = ProcessAccessor(node.Type, propName, get);
            accessors = accessors.ReplaceNode(get, newGet);
            return node.WithAccessorList(accessors);
        });

    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node) 
        => Rewrite(node, node => {
            var propName = node.Identifier;
            var accessors = node.AccessorList;

            if (accessors.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) is not AccessorDeclarationSyntax get) {
                return base.VisitPropertyDeclaration(node);
            }
            if (get.Body is null) {
                return base.VisitPropertyDeclaration(node);
            }

            var newGet = ProcessAccessor(node.Type, propName, get);
            accessors = accessors.ReplaceNode(get, newGet);
            return node.WithAccessorList(accessors);
        });

    AccessorDeclarationSyntax ProcessAccessor(TypeSyntax type, SyntaxToken propName, AccessorDeclarationSyntax get)
    {
        if (TryUseExpressionBody(get.Body, propName) is ArrowExpressionClauseSyntax expr) {
            return get.WithBody(null).WithExpressionBody(expr).WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        }
        else {
            return get.WithBody(ProcessGetBody(get.Body, propName, type));
        }
    }


    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        => Rewrite(node, node => {
            if (node.ReturnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)) {
                return base.VisitMethodDeclaration(node);
            }
            if (node.Body is null) {
                return base.VisitMethodDeclaration(node);
            }

            var newMethod = node;

            if (TryUseExpressionBody(node.Body, node.Identifier) is ArrowExpressionClauseSyntax expr) {
                return node.WithBody(null).WithExpressionBody(expr).WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            }
            else {
                newMethod = node.WithBody(ProcessGetBody(node.Body, node.Identifier, node.ReturnType));
            }

            return newMethod;
        });

    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
        => Rewrite(node, node => {
            if (node.Expression is null) {
                var method = node.Ancestors().OfType<BlockSyntax>().Last();
                if (method.GetAnnotations("ReturnVar").FirstOrDefault() is SyntaxAnnotation returnVar) {
                    return ReturnStatement(IdentifierName(returnVar.Data));
                }
                else if (method.GetAnnotations("ReturnDefault").Any()) {
                    return ReturnStatement(LiteralExpression(SyntaxKind.DefaultLiteralExpression));
                }
            }

            return base.VisitReturnStatement(node);
        });

    ArrowExpressionClauseSyntax TryUseExpressionBody(BlockSyntax body, SyntaxToken prop)
        => body.Statements.Count == 1
            && body.Statements[0] is ExpressionStatementSyntax exprst
            && exprst.Expression is AssignmentExpressionSyntax assignment
            && assignment.Left is IdentifierNameSyntax name
            && name.Identifier.Text == prop.Text
        ? ArrowExpressionClause(assignment.Right)
        : null;

    BlockSyntax ProcessGetBody(BlockSyntax body, SyntaxToken propName, TypeSyntax propType)
    {
        propName = valueName ?? propName;

        if (body.Statements[^1] is ExpressionStatementSyntax exst
            && exst.Expression is AssignmentExpressionSyntax assign
            && assign.Left is IdentifierNameSyntax identifier
            && identifier.Identifier.Text == propName.Text) {

            body = body.ReplaceNode(exst, ReturnStatement(assign.Right));
        }

        var uses = body.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.Text == propName.Text);
        if (uses) {
            var newBody = Block().AddStatements(
                LocalDeclarationStatement(
                    VariableDeclaration(propType, SingletonSeparatedList(
                        VariableDeclarator(propName)
                            .WithInitializer(
                                EqualsValueClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression)))))));

            newBody = newBody.AddStatements([.. body.Statements]);

            if (body.Statements[^1] is not ReturnStatementSyntax) {
                newBody = newBody.AddStatements(ReturnStatement(IdentifierName(propName)));
            }

            body = newBody.WithAdditionalAnnotations(new SyntaxAnnotation("ReturnVar", propName.Text));
        }
        else {
            body = body.WithAdditionalAnnotations(new SyntaxAnnotation("ReturnDefault"));
        }

        return (BlockSyntax)base.Visit(body);
    }
}
