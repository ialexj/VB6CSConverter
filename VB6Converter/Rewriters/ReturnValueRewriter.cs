using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using System;
using System.IO;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class ReturnValueRewriter : CSharpSyntaxRewriter
{
    readonly ILogger Log = VB6Converter.Log.Default;

    public override SyntaxNode DefaultVisit(SyntaxNode node)
    {
        try {
            return base.DefaultVisit(node);
        }
        catch (Exception ex) {
            Log.Error(ex, "Couldn't process {node} in {file}.", node, Path.GetFileNameWithoutExtension(node.SyntaxTree.FilePath));
            throw;
        }
    }

    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var propName  = IdentifierName(node.Identifier);
        var accessors = node.AccessorList;

        if (accessors.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) is not AccessorDeclarationSyntax get) {
            return base.VisitPropertyDeclaration(node);
        }

        if (get.Body is null) {
            return base.VisitPropertyDeclaration(node);
        }

        var newGet = get;

        if (get.Body.Statements.Count == 1
            && get.Body.Statements[0] is ExpressionStatementSyntax exprst
            && exprst.Expression is AssignmentExpressionSyntax assignment
            && assignment.Left is IdentifierNameSyntax name
            && name.Identifier.Text == node.Identifier.Text) {

            newGet = get.WithBody(null)
                .WithExpressionBody(ArrowExpressionClause(assignment.Right))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        }
        else {
            var newBody = ProcessBody(get.Body, node.Identifier, node.Type);
            newGet = get.WithBody(newBody);
        }

        accessors = accessors.ReplaceNode(get, newGet);
        return node.WithAccessorList(accessors);
    }

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.ReturnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)) {
            return base.VisitMethodDeclaration(node);
        }

        if (node.Body is null) {
            return base.VisitMethodDeclaration(node);
        }

        var newMethod = node;

        if (node.Body.Statements.Count == 1
            && node.Body.Statements[0] is ExpressionStatementSyntax exprst
            && exprst.Expression is AssignmentExpressionSyntax assignment
            && assignment.Left is IdentifierNameSyntax name
            && name.Identifier.Text == node.Identifier.Text) {

            newMethod = node.WithBody(null)
                .WithExpressionBody(ArrowExpressionClause(assignment.Right))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        }
        else {
            var newBody = ProcessBody(node.Body, node.Identifier, node.ReturnType);
            newMethod = node.WithBody(newBody);
        }

        return newMethod;
    }

    BlockSyntax ProcessBody(BlockSyntax body, SyntaxToken propName, TypeSyntax propType)
    {
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

    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node.Expression is null) {
            var method = node.Ancestors().OfType<BlockSyntax>().First();
            if (method.GetAnnotations("ReturnVar").FirstOrDefault() is SyntaxAnnotation returnVar) {
                return ReturnStatement(IdentifierName(returnVar.Data));
            }
            else if (method.GetAnnotations("ReturnDefault").Any()) {
                return ReturnStatement(LiteralExpression(SyntaxKind.DefaultLiteralExpression));
            }
        }

        return base.VisitReturnStatement(node);
    }
}
