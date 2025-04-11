using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.Xml.Linq;

namespace VB6Converter;

public class ReturnValueRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node.Expression is null) {
            var parent = node.Ancestors().FirstOrDefault(a => a.GetAnnotations("PropName").Any());
            if (parent != null) {
                var annot = parent.GetAnnotations("PropName").First();
                return ReturnStatement(IdentifierName(annot.Data));
            }
        }

        return base.VisitReturnStatement(node);
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

        var uses = body.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.Text == propName.Text);

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

            body = newBody;
        }

        body = body.WithAdditionalAnnotations(new SyntaxAnnotation("PropName", propName.Text));
        return (BlockSyntax)base.Visit(body);
    }
}
