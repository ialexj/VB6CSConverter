using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;

public class MissingTypeScanner(SemanticModel semantics, MissingTypes types) : CSharpSyntaxWalker
{
    readonly ILogger _log = Log.Default.ForContext("file", System.IO.Path.GetFileNameWithoutExtension(semantics.SyntaxTree.FilePath));

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var log = _log.ForContext("node", node);

        var symbol = semantics.GetSymbolInfo(node);
        if (symbol.Symbol is null && symbol.CandidateSymbols.Length == 0) {
            // Figure out what type this member is attached to.
            var typeInfo = semantics.GetTypeInfo(node.Expression).ConvertedType;
            if (typeInfo is null || string.IsNullOrEmpty(typeInfo.Name)) {
                return;
            }

            // When targetting `this`, use the base class instead.
            var isThis = node.Expression.DescendantNodesAndSelf().OfType<ThisExpressionSyntax>().Any();
            if (isThis) {
                typeInfo = typeInfo.BaseType;
            }

            // Method or property?
            var invocation = node.Ancestors()
                .SkipWhile(a => a is MemberAccessExpressionSyntax)
                .FirstOrDefault() as InvocationExpressionSyntax;
            
            if (invocation is not null) {
                TypeSyntax returnType = PredefinedType(Token(SyntaxKind.ObjectKeyword));

                if (invocation.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is AssignmentExpressionSyntax assign && assign.Right.Contains(invocation)) {
                    var leftSymbol = semantics.GetTypeInfo(assign.Left).ConvertedType;
                    if (leftSymbol is not null) {
                        returnType = ParseTypeName(leftSymbol.ToString());
                    }
                }

                var parameters = invocation.ArgumentList.Arguments.Select(a => {
                    var parameterType = semantics.GetTypeInfo(a.Expression).ConvertedType;
                    return Parameter(Identifier($"p{invocation.ArgumentList.Arguments.IndexOf(a)}"))
                        .WithType(parameterType != null
                            ? ParseTypeName(parameterType.ToString())
                            : PredefinedType(Token(SyntaxKind.ObjectKeyword)));
                });

                types.RegisterMethod(typeInfo.ToString(), node.Name, parameters, returnType);
            }
            else {
                TypeSyntax targetType = PredefinedType(Token(SyntaxKind.ObjectKeyword));

                if (node.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is AssignmentExpressionSyntax assignment) {
                    if (semantics.GetTypeInfo(assignment.Right).ConvertedType is ITypeSymbol ts) {
                        if (ParseTypeName(ts.ToString()) is TypeSyntax t) {
                            targetType = t;
                        }
                    }
                }

                types.RegisterProperty(typeInfo.ToString(), node.Name, targetType);
            }
        }
    }

    public override void VisitQualifiedName(QualifiedNameSyntax node)
    {
        var log = _log.ForContext("node", node);

        try {
            var parent = node.Ancestors().SkipWhile(a => a is QualifiedNameSyntax).FirstOrDefault();
            if (parent is BaseNamespaceDeclarationSyntax or UsingDirectiveSyntax) {
                return;
            }

            bool isObject = node.Parent is BaseTypeSyntax or VariableDeclarationSyntax;

            var symbol = semantics.GetSymbolInfo(node);
            if (symbol.Symbol is null && symbol.CandidateSymbols.Length == 0) {
                types.RegisterIdentifier(node.Right.Identifier, node.Left, isObject);
            }
        }
        catch (Exception ex) {
            log.ForContext("message", ex.Message)
                .Warning("{file}: Couldn't visit identifer {node}: {message}");
        }
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        var log = _log.ForContext("node", node);

        try {
            if (node.IsVar) {
                return;
            }
            if (node.Parent is QualifiedNameSyntax or UsingDirectiveSyntax or BaseNamespaceDeclarationSyntax) {
                return;
            }

            bool isObject = node.Parent is BaseTypeSyntax or VariableDeclarationSyntax;

            var symbol = semantics.GetSymbolInfo(node);
            if (symbol.Symbol is null && symbol.CandidateSymbols.Length == 0) {
                types.RegisterIdentifier(node.Identifier, node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()?.Name, isObject);
            }
        }
        catch (Exception ex) {
            log.ForContext("message", ex.Message)
                .Warning("{file}: Couldn't visit identifer {node}: {message}");
        }
    }
}
