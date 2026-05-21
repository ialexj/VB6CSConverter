#nullable enable
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace ComStubGenerator;

/// <summary>
/// Post-processes a generated C# stub <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax"/>
/// and removes COM infrastructure members that are unlikely to be called by user code:
/// <list type="bullet">
///   <item>The <c>IDispatch</c> and <c>IUnknown</c> interface declarations themselves.</item>
///   <item>
///     Methods inherited from those interfaces that COM type libraries surface on other types:
///     <c>AddRef</c>, <c>Release</c>, <c>QueryInterface</c>, <c>GetIDsOfNames</c>,
///     <c>GetTypeInfo</c>, <c>GetTypeInfoCount</c>, and <c>Invoke</c> (8-parameter IDispatch overload only).
///   </item>
/// </list>
/// Applied after <see cref="MscorlibTypeNormalizingRewriter"/> when COM-plumbing filtering is enabled.
/// </summary>
internal sealed class ComPlumbingFilterRewriter : CSharpSyntaxRewriter
{
    // Interface names to suppress entirely.
    static readonly HashSet<string> BlockedInterfaces = new(System.StringComparer.Ordinal)
    {
        "IDispatch",
        "IUnknown",
    };

    // Method names that are unambiguously COM infrastructure — safe to filter by name alone.
    static readonly HashSet<string> BlockedByName = new(System.StringComparer.Ordinal)
    {
        "AddRef",
        "Release",
        "QueryInterface",
        "GetIDsOfNames",
        "GetTypeInfo",
        "GetTypeInfoCount",
    };

    // IDispatch.Invoke has exactly 8 parameters:
    //   dispidMember, riid, lcid, wFlags, pdispparams, pvarResult, pexcepinfo, puArgErr
    // A parameter-count guard avoids removing legitimate scripting-style Invoke methods
    // (e.g. IScriptControl.Invoke) that have different signatures.
    const string InvokeMethodName = "Invoke";
    const int IDispatchInvokeParameterCount = 8;

    public override Microsoft.CodeAnalysis.SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        if (BlockedInterfaces.Contains(node.Identifier.Text))
            return null;

        return base.VisitInterfaceDeclaration(node);
    }

    public override Microsoft.CodeAnalysis.SyntaxNode? VisitBaseList(BaseListSyntax node)
    {
        // CSharpSyntaxRewriter does not support returning null from visitors for elements
        // of a SeparatedSyntaxList, so filter the types directly rather than via VisitSimpleBaseType.
        var filtered = node.Types
            .Where(t => !(t.Type is IdentifierNameSyntax id && BlockedInterfaces.Contains(id.Identifier.Text)))
            .ToList();

        if (filtered.Count == 0)
            return null;  // drop the colon + base list entirely from the parent node

        if (filtered.Count == node.Types.Count)
            return base.VisitBaseList(node);  // nothing removed; visit children normally

        // Rebuild the separated list with commas inferred by SeparatedList().
        var newBaseList = node.WithTypes(
            Microsoft.CodeAnalysis.CSharp.SyntaxFactory.SeparatedList<BaseTypeSyntax>(filtered));

        return base.VisitBaseList(newBaseList);
    }

    public override Microsoft.CodeAnalysis.SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        string name = node.Identifier.Text;

        if (BlockedByName.Contains(name))
            return null;

        if (name == InvokeMethodName && node.ParameterList.Parameters.Count == IDispatchInvokeParameterCount)
            return null;

        return base.VisitMethodDeclaration(node);
    }
}
