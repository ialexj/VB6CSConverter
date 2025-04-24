using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Rewriters;

public class ReferenceMemberRewriter : CSharpSyntaxRewriter
{


    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        return base.VisitMemberAccessExpression(node);
    }
}
