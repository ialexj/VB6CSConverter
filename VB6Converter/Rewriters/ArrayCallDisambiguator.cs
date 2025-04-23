using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using System.IO;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters;
public class ArrayCallDisambiguator(SemanticModel model) : CSharpSyntaxRewriter
{
    readonly ILogger _log = Log.Default.ForContext("file", Path.GetFileNameWithoutExtension(model.SyntaxTree.FilePath));

    // Correct calls that should actually be array access
    public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var log = _log.ForContext("node", node);

        var symbol = model.GetSymbolInfo(node.Expression);

        bool isArray = false;
        if (symbol.Symbol is ILocalSymbol local && local.Type.Kind == SymbolKind.ArrayType) {
            isArray = true;
        }
        else if (symbol.CandidateSymbols.Any(f => f is IFieldSymbol fs && fs.Type.Kind == SymbolKind.ArrayType)) {
            isArray = true;
        }
        else if (symbol.CandidateReason == CandidateReason.NotInvocable) {
            isArray = true;
        }

        if (isArray) {
            log.Debug("{file}: Making invocation '{node}' into an array access.", 
                Path.GetFileNameWithoutExtension(node.SyntaxTree.FilePath), node.ToString());
            return ElementAccessExpression(node.Expression, BracketedArgumentList(node.ArgumentList.Arguments));
        }
        else {
            return base.VisitInvocationExpression(node);
        }
    }

    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var log = _log.ForContext("node", node);

        if (node.Parent is not InvocationExpressionSyntax) {
            var symbol = model.GetSymbolInfo(node.Name);
            if (symbol.Symbol is null && symbol.CandidateSymbols.Any(c => c.Kind == SymbolKind.Method)) {
                log.Debug("{file}: Making member '{node}' into an invocation.");
                return InvocationExpression(node);
            }
        }

        return base.VisitMemberAccessExpression(node);
    }


    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        var log = _log.ForContext("node", node);

        var parents = node.Ancestors().SkipWhile(a => a is MemberAccessExpressionSyntax or NameSyntax);
        if (parents.FirstOrDefault() is InvocationExpressionSyntax) {
            log.Verbose("{file}: Identifier '{node}' is already part of an invocation.");
            return base.VisitIdentifierName(node);
        }
        
        var symbol = model.GetSymbolInfo(node);
        if (symbol.Symbol is null && symbol.CandidateSymbols.Any(c => c.Kind == SymbolKind.Method)) {
            log.Debug("{file}: Making identifier '{node}' into an invocation.");
            return InvocationExpression(node);
        }

        return base.VisitIdentifierName(node);

    }
}
