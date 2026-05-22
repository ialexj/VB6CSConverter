using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using System.Reflection;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;
public class DAORewriter(SemanticModel semantics) : LoggedRewriter
{
    const string DaoNamespace = "Microsoft.Office.Interop.Access.Dao";

    static readonly string[] RecordsetOptionEnum = [
        "dbDenyWrite",
        "dbDenyRead",
        "dbReadOnly",
        "dbAppendOnly",
        "dbInconsistent",
        "dbConsistent",
        "dbSQLPassThrough",
        "dbFailOnError",
        "dbForwardOnly ",
        "dbSeeChanges",
        "dbRunAsync",
        "dbExecDirect"
    ];

    static readonly string[] RecordsetTypeEnum = [
        "dbOpenTable",
        "dbOpenSnapshot",
        "dbOpenForwardOnly",
        "dbOpenDynamic",
        "dbOpenDynaset",
        "dbOpenKeyset"
    ];

    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Log.Rewrite(this, node, node => {
            // Run other rewrites first

            if (node.Left is MemberAccessExpressionSyntax macc) {
                var type = semantics.GetTypeInfo(macc.Expression);
                if (type.ConvertedType is ITypeSymbol t) {
                    if (t.Name == "Recordset") {
                        if (string.Equals(macc.Name.Identifier.Text, "Bookmark", StringComparison.OrdinalIgnoreCase)) {
                            return InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    macc.Expression, IdentifierName("set_Bookmark")),
                                ArgumentList(
                                    SingletonSeparatedList(
                                        Argument(node.Right))));
                        }
                    }
                }
            }
            //else if (node.Right is MemberAccessExpressionSyntax rmacc && rmacc.GetAnnotations("FieldAccess").Any()) {
            //    var leftType = semantics.GetTypeInfo(node.Left);
            //    node = node.WithRight(CastExpression(leftType.ToTypeSyntax(), rmacc));
            //}

            return base.VisitAssignmentExpression(node);
        });

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Log.Rewrite(this, node, node => {
            var type = semantics.GetTypeInfo(node.Expression);

            if (type.ConvertedType is not null && type.ConvertedType.Name == "Recordset") {
            }

            return base.VisitMemberAccessExpression(node);
        });

    public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        => Log.Rewrite(this, node, node => {
            var type = semantics.GetTypeInfo(node.Expression);

            if (type.ConvertedType is ITypeSymbol t && t.ContainingNamespace?.ToString() == DaoNamespace) {
                if (type.ConvertedType.Name == "Recordset") {
                    return MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        ElementAccessExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                node.Expression,
                                IdentifierName("Fields")
                            ),
                            node.ArgumentList),
                        IdentifierName("Value"))
                        .WithAdditionalAnnotations(new SyntaxAnnotation("FieldAccess"));
                }
                
            }
            else if (node.Expression is MemberAccessExpressionSyntax ma) {
                var parentType = semantics.GetTypeInfo(ma.Expression);
                if (parentType.ConvertedType is ITypeSymbol pt && pt.ContainingNamespace?.ToString() == DaoNamespace) {
                    if (string.Equals(pt.Name, "QueryDef")) {
                        if (string.Equals(ma.Name.Identifier.Text, "Parameters", StringComparison.OrdinalIgnoreCase)) {
                            return MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                node.ReplaceNode(ma, ma.WithName(IdentifierName("Parameters"))),
                                IdentifierName("Value"));
                        }
                    }
                }
            }

            return base.VisitElementAccessExpression(node);
        });

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        => Log.Rewrite(this, node, node => {
            if (node.Left is SimpleNameSyntax name && name.Identifier.Text == "DAO") {
                return node.Right;
            }

            return base.VisitQualifiedName(node);
        });

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Log.Rewrite(this, node, node => {
            if (node.FirstAncestorOrSelf<TypeSyntax>() is TypeSyntax type) {
                if (node.Identifier.Text.Equals("Recordset", StringComparison.CurrentCultureIgnoreCase)) {
                    return node.WithUsingDAO();
                }

                if (node.Identifier.Text.Equals("QueryDef", StringComparison.CurrentCultureIgnoreCase)) {
                    return node.WithUsingDAO();
                }

                if (node.Identifier.Text.Equals("Field", StringComparison.CurrentCultureIgnoreCase)) {
                    return node.WithUsingDAO();
                }
            }

            if (node.Parent is MemberAccessExpressionSyntax or QualifiedNameSyntax) {
                return base.VisitIdentifierName(node);
            }

            if (RecordsetOptionEnum.Contains(node.Identifier.Text, StringComparer.InvariantCultureIgnoreCase)) {
                return MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("RecordsetOptionEnum"), IdentifierName(node.Identifier.Text))
                    .WithUsingDAO();
            }

            if (RecordsetTypeEnum.Contains(node.Identifier.Text, StringComparer.InvariantCultureIgnoreCase)) {
                return MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("RecordsetTypeEnum"), IdentifierName(node.Identifier.Text))
                    .WithUsingDAO();
            }

            return base.VisitIdentifierName(node);
        });
}

public static class DaoExtensions
{
    public static T WithUsingDAO<T>(this T node) where T : SyntaxNode
    {
        if (!node.HasAnnotations("Using")) {
            return node.WithAdditionalAnnotations(
                new SyntaxAnnotation("Using", "Microsoft.Office.Interop.Access.Dao"),
                new SyntaxAnnotation("IgnoreMissing", string.Empty)
            );
        }
        else {
            return node;
        }
    }
}
