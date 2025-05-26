using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class MissingTypeScanner(SemanticModel semantics, MissingTypes types) : CSharpSyntaxWalker
{
    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        {
            // Does this expression refer to a valid symbol?
            var symbol = semantics.GetSymbolInfo(node);
            if (symbol.Symbol is not null || symbol.CandidateSymbols.Length > 0) {
                base.VisitMemberAccessExpression(node);
                return;
            }
        }

        // Figure out what type this member is attached to.
        MissingTypes.TypeDef typeDef;
        bool isStatic = false;

        {
            var typeInfo = semantics.GetTypeInfo(node.Expression).ConvertedType;

            if (typeInfo is null || string.IsNullOrEmpty(typeInfo.Name)) {
                if (node.Expression is IdentifierNameSyntax root) {
                    var symbol = semantics.GetSymbolInfo(root);
                    if (symbol.Symbol is null && symbol.CandidateSymbols.Length == 0) {
                        isStatic = true; // Accessing a static member on an unknown class
                    }

                    typeDef = types.RegisterType(root.Identifier.Text, null);
                }
                else {
                    base.VisitMemberAccessExpression(node);
                    return;
                }
            }
            else {
                if (typeInfo.SpecialType != SpecialType.None) {
                    base.VisitMemberAccessExpression(node);
                    return;
                }

                // When targetting `this`, use the base class instead.
                if (node.Expression is ThisExpressionSyntax && typeInfo.BaseType is not null) {
                    typeInfo = typeInfo.BaseType;
                }

                var ns = !typeInfo.ContainingNamespace.IsGlobalNamespace ? ParseName(typeInfo.ContainingNamespace.ToString()) : null;

                typeDef = types.RegisterType(typeInfo.Name, ns);
            }
        }
        
        // Method or property?
        var invocation = node.Ancestors()
            .SkipWhile(a => a is MemberAccessExpressionSyntax)
            .FirstOrDefault() as InvocationExpressionSyntax;
            
        if (invocation is not null) {
            typeDef.AddMethod(node.Name.Identifier, ToParameters(invocation.ArgumentList), GetReturnType(node), isStatic);
        }
        else {
            typeDef.AddProperty(node.Name.Identifier, GetReturnType(node), isStatic);
        }
    }

    TypeSyntax GetReturnType(SyntaxNode node)
    {
        TypeSyntax returnType = PredefinedType(Token(SyntaxKind.ObjectKeyword));

        if (node.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is AssignmentExpressionSyntax assign
            && assign.Right.Contains(node)) {
            returnType = semantics.GetTypeInfo(assign.Left).ToTypeSyntax();
        }

        return returnType;
    }

    ParameterListSyntax ToParameters(ArgumentListSyntax arguments)
    {
        return ParameterList(SeparatedList(arguments.Arguments.Select(a => {
            var ptype = semantics.GetTypeInfo(a.Expression).ToTypeSyntax();
            return Parameter(Identifier($"p{arguments.Arguments.IndexOf(a)}")).WithType(ptype);
        })));
    }

    public override void VisitQualifiedName(QualifiedNameSyntax node)
    {
        var parent = node.Ancestors().SkipWhile(a => a is QualifiedNameSyntax).FirstOrDefault();
        if (parent is BaseNamespaceDeclarationSyntax or UsingDirectiveSyntax) {
            return;
        }

        if (node.GetAnnotations("MissingMembers").Any()) {
            return;
        }

        var symbol = semantics.GetSymbolInfo(node);
        if (symbol.Symbol is null && symbol.CandidateSymbols.Length == 0) {
            bool isObject = node.Parent is BaseTypeSyntax or VariableDeclarationSyntax or ParameterSyntax or ObjectCreationExpressionSyntax;
            if (isObject) {
                types.RegisterType(node.Right.Identifier.Text, node.Left);
            }
            else {
                //Debugger.Break();
            }
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax or UsingDirectiveSyntax or BaseNamespaceDeclarationSyntax or NameColonSyntax) {
            return;
        }
        if (node.IsVar) {
            return;
        }
        if (node.GetAnnotations("MissingMembers").Any()) {
            return;
        }

        var symbol = semantics.GetSymbolInfo(node);
        if (symbol.Symbol is not null || symbol.CandidateSymbols.Length > 0) {
            return;
        }

        if (node.Parent is InvocationExpressionSyntax invocation) {
            types.RegisterMethod(types.RegisterType("_Functions"), node.Identifier, ToParameters(invocation.ArgumentList), GetReturnType(invocation), isStatic: true);
        }
        else {
            bool isObject = node.Parent is BaseTypeSyntax or VariableDeclarationSyntax or ParameterSyntax or ObjectCreationExpressionSyntax;
            if (isObject) {
                types.RegisterType(node.Identifier.Text);
            }
            else if (!string.IsNullOrEmpty(node.Identifier.Text)) {
                types.RegisterType("_Constants").RegisterConstant(node.Identifier, GetReturnType(node));
            }
        }
    }
}
