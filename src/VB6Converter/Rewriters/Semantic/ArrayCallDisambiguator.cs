using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;
public class ArrayCallDisambiguator(SemanticModel model) : LoggedRewriter
{
    static bool HasIndexer(ITypeSymbol type)
        => type?.GetMembers().Any(m => m is IPropertySymbol { IsIndexer: true }) == true;

    // Correct calls that should actually be array access
    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        => Log.Rewrite(this, node, node => {
            var symbol = model.GetSymbolInfo(node.Expression);
            if (symbol.Symbol is IMethodSymbol) {
                return base.VisitInvocationExpression(node);
            }

            bool isElementAccess = false;
            if (symbol.Symbol is ILocalSymbol local && local.Type.Kind == SymbolKind.ArrayType){
                isElementAccess = true;
            }
            else if (HasIndexer(model.GetTypeInfo(node.Expression).Type) || HasIndexer(model.GetTypeInfo(node.Expression).ConvertedType)) {
                isElementAccess = true;
            }
            else if (symbol.CandidateSymbols.Any(f => f is IFieldSymbol fs && fs.Type.Kind == SymbolKind.ArrayType)) {
                isElementAccess = true;
            }
            else if (symbol.CandidateReason == CandidateReason.NotInvocable) {
                isElementAccess = true;
            }
            else {
                // Check only the name/identifier being invoked, not sub-expressions,
                // to avoid picking up CS1955 from a nested invocation (e.g. the inner
                // aFichaCliente(1) in (aFichaCliente(1)).ToString()).
                var callableSpan = node.Expression is MemberAccessExpressionSyntax ma
                    ? ma.Name.Span
                    : node.Expression.Span;
                var diagnostics = model.GetDiagnostics(callableSpan);
                if (diagnostics.Any(d => d.Id == "CS1955")) { // non-invokable member
                    isElementAccess = true;
                }
            }

            if (isElementAccess) {
                return ElementAccessExpression(node.Expression, BracketedArgumentList(node.ArgumentList.Arguments));
            }
            else {
                return base.VisitInvocationExpression(node);
            }
        });

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Log.Rewrite(this, node, node => {
            if (node.Parent is not InvocationExpressionSyntax) {
                var symbol = model.GetSymbolInfo(node.Name);
                if (symbol.Symbol is null && symbol.CandidateSymbols.Any(c => c.Kind == SymbolKind.Method)) {
                    return InvocationExpression(node);
                }
            }

            return base.VisitMemberAccessExpression(node);
        });


    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        => Log.Rewrite(this, node, node => {
            var parents = node.Ancestors().SkipWhile(a => a is MemberAccessExpressionSyntax or NameSyntax);
            if (parents.FirstOrDefault() is InvocationExpressionSyntax) {
                return base.VisitIdentifierName(node);
            }

            var symbol = model.GetSymbolInfo(node);
            if (symbol.Symbol is null && symbol.CandidateSymbols.Any(c => c.Kind == SymbolKind.Method)) {
                return InvocationExpression(node);
            }

            return base.VisitIdentifierName(node);

        });
}
