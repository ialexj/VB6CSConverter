#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ComStubGenerator;

/// <summary>
/// Post-processes a generated C# stub <see cref="CompilationUnitSyntax"/> and collapses
/// <c>add_X</c> / <c>remove_X</c> method pairs into a single <c>event T X;</c> declaration.
/// <para>
/// .NET components registered in COM expose their events as a pair of void methods:
/// <code>
/// public void add_Disposed(System.EventHandler value);
/// public void remove_Disposed(System.EventHandler value);
/// </code>
/// C# interfaces that require those events (e.g. <c>System.ComponentModel.IComponent</c>)
/// declare them as <c>event</c> members, so the stub must match.
/// </para>
/// <para>
/// A pair is collapsed when both of the following hold:
/// <list type="bullet">
///   <item>Both methods return <c>void</c> and have exactly one parameter.</item>
///   <item>The single parameter has the same type string in both methods.</item>
/// </list>
/// Unpaired <c>add_*</c> / <c>remove_*</c> methods, or pairs whose parameter types differ,
/// are left untouched.
/// </para>
/// </summary>
internal sealed class AddRemoveEventCollapsingRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
        var newMembers = CollapseEvents(visited.Members, isInterface: false);
        return newMembers == visited.Members ? visited : visited.WithMembers(newMembers);
    }

    public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        var visited = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!;
        var newMembers = CollapseEvents(visited.Members, isInterface: true);
        return newMembers == visited.Members ? visited : visited.WithMembers(newMembers);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Core collapsing logic
    // ──────────────────────────────────────────────────────────────────────

    static SyntaxList<MemberDeclarationSyntax> CollapseEvents(
        SyntaxList<MemberDeclarationSyntax> members,
        bool isInterface)
    {
        // Index add_* and remove_* candidates by base name.
        var addMethods    = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);
        var removeMethods = new Dictionary<string, MethodDeclarationSyntax>(StringComparer.Ordinal);

        foreach (var member in members.OfType<MethodDeclarationSyntax>())
        {
            string name = member.Identifier.Text;
            if (IsVoidSingleParam(member))
            {
                if (name.StartsWith("add_", StringComparison.Ordinal))
                    addMethods[name["add_".Length..]] = member;
                else if (name.StartsWith("remove_", StringComparison.Ordinal))
                    removeMethods[name["remove_".Length..]] = member;
            }
        }

        // Determine which base names form a valid event pair.
        var eventPairs = new Dictionary<string, (MethodDeclarationSyntax Add, MethodDeclarationSyntax Remove)>(
            StringComparer.Ordinal);

        foreach (var (baseName, addMethod) in addMethods)
        {
            if (removeMethods.TryGetValue(baseName, out var removeMethod)
                && GetSingleParamType(addMethod) == GetSingleParamType(removeMethod))
            {
                eventPairs[baseName] = (addMethod, removeMethod);
            }
        }

        if (eventPairs.Count == 0)
            return members;  // nothing to collapse — return original (reference-equal) list

        // Build the set of method nodes to absorb into events.
        var absorbed = new HashSet<MethodDeclarationSyntax>(ReferenceEqualityComparer.Instance);
        foreach (var (add, remove) in eventPairs.Values)
        {
            absorbed.Add(add);
            absorbed.Add(remove);
        }

        // Rebuild the member list, replacing the add_ method with an event declaration
        // and dropping the paired remove_ method.
        var result = new List<MemberDeclarationSyntax>(members.Count);

        foreach (var member in members)
        {
            if (member is MethodDeclarationSyntax method && absorbed.Contains(method))
            {
                string name = method.Identifier.Text;
                if (name.StartsWith("add_", StringComparison.Ordinal))
                {
                    // Emit the event declaration in place of the add_ method.
                    string baseName   = name["add_".Length..];
                    string eventType  = GetSingleParamType(method);
                    result.Add(BuildEvent(baseName, eventType, isInterface, method));
                }
                // remove_ method is intentionally dropped (absorbed by the event).
            }
            else
            {
                result.Add(member);
            }
        }

        return List(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    static bool IsVoidSingleParam(MethodDeclarationSyntax method)
        => method.ReturnType is PredefinedTypeSyntax p
           && p.Keyword.IsKind(SyntaxKind.VoidKeyword)
           && method.ParameterList.Parameters.Count == 1;

    static string GetSingleParamType(MethodDeclarationSyntax method)
        => method.ParameterList.Parameters[0].Type!.ToString();

    static EventFieldDeclarationSyntax BuildEvent(
        string eventName,
        string delegateType,
        bool isInterface,
        MethodDeclarationSyntax sourceMethod)
    {
        var eventDecl = EventFieldDeclaration(
            VariableDeclaration(ParseTypeName(delegateType))
                .WithVariables(SingletonSeparatedList(
                    VariableDeclarator(Identifier(eventName)))));

        // Interfaces carry no explicit accessibility modifier on their members;
        // classes carry the same modifier as the absorbed add_ method (typically public).
        if (!isInterface)
        {
            var accessibilityModifiers = sourceMethod.Modifiers
                .Where(t => t.IsKind(SyntaxKind.PublicKeyword)
                         || t.IsKind(SyntaxKind.ProtectedKeyword)
                         || t.IsKind(SyntaxKind.InternalKeyword)
                         || t.IsKind(SyntaxKind.PrivateKeyword)
                         || t.IsKind(SyntaxKind.StaticKeyword))
                .ToArray();

            if (accessibilityModifiers.Length > 0)
                eventDecl = eventDecl.WithModifiers(TokenList(accessibilityModifiers));
        }

        return eventDecl;
    }
}
