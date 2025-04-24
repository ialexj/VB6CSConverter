using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;
public class DAORewriter(SemanticModel semantics) : CSharpSyntaxRewriter
{
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

    //public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    //{
    //    var type = semantics.GetTypeInfo(node.Expression);

    //    if (type.ConvertedType is not null && type.ConvertedType.Name == "Recordset") {
    //        var name = node.Name.Identifier.ValueText;
    //        if (RecordsetMembers.TryGetValue(name, out var reference) && !string.Equals(name, reference, StringComparison.CurrentCulture)) {
    //            node = node.WithName(IdentifierName(reference));
    //        }
    //    }

    //    return base.VisitMemberAccessExpression(node);
    //}

    public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var type = semantics.GetTypeInfo(node.Expression);

        if (type.ConvertedType is not null && type.ConvertedType.Name == "Recordset") {
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                ElementAccessExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        node.Expression, 
                        IdentifierName("Fields")
                    ),
                    node.ArgumentList),
                IdentifierName("Value")
            );
        }

        return base.VisitElementAccessExpression(node);
    }

    public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
    {
        if (node.Left is SimpleNameSyntax name && name.Identifier.Text == "DAO") {
            return node.Right;
        }

        return base.VisitQualifiedName(node);
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
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
    }
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
