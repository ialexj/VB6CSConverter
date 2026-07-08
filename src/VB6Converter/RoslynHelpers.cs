using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter;

internal static class RoslynHelpers
{
    /// <summary>
    /// Determines whether a <see cref="NameSyntax"/> appears in a syntactic position where
    /// it is expected to denote a type (as opposed to a variable/member value expression).
    /// </summary>
    public static bool IsTypeUsage(this NameSyntax node)
        => node.Parent switch {
            VariableDeclarationSyntax v when v.Type == node => true,
            ParameterSyntax p when p.Type == node => true,
            PropertyDeclarationSyntax p when p.Type == node => true,
            MethodDeclarationSyntax m when m.ReturnType == node => true,
            LocalFunctionStatementSyntax l when l.ReturnType == node => true,
            ForEachStatementSyntax f when f.Type == node => true,
            IndexerDeclarationSyntax i when i.Type == node => true,
            ObjectCreationExpressionSyntax o when o.Type == node => true,
            CastExpressionSyntax c when c.Type == node => true,
            ArrayTypeSyntax a when a.ElementType == node => true,
            NullableTypeSyntax n when n.ElementType == node => true,
            PointerTypeSyntax p when p.ElementType == node => true,
            RefTypeSyntax r when r.Type == node => true,
            BaseTypeSyntax b when b.Type == node => true,
            DeclarationPatternSyntax d when d.Type == node => true,
            MemberAccessExpressionSyntax m when m.Expression == node => true,
            TypeArgumentListSyntax => true,
            _ => false,
        };

    public static CompilationUnitSyntax CompilationUnit(
        ClassDeclarationSyntax cls, NameSyntax ns = null)
        => SyntaxFactory.CompilationUnit()
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    ns is not null
                        ? FileScopedNamespaceDeclaration(ns)
                            .WithMembers(SingletonList<MemberDeclarationSyntax>(cls))
                        : cls))
            .NormalizeWhitespace();

    public static IEnumerable<ITypeSymbol> FindTypesByName(SemanticModel sem, string name, INamespaceSymbol nss = null)
    {
        nss ??= sem.Compilation.GlobalNamespace;

        foreach (var m in nss.GetTypeMembers()) {
            if (string.Equals(m.ToString(), name, StringComparison.OrdinalIgnoreCase)) {
                yield return m;
            }
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) {
                yield return m;
            }
        }

        foreach (var nested in nss.GetNamespaceMembers()) {
            foreach (var ts in FindTypesByName(sem, name, nested)) {
                yield return ts;
            }
        }
    }

    /// <summary>
    /// Recursively searches the compilation's namespaces for a type whose name (or fully
    /// qualified name) matches <paramref name="name"/> case-insensitively. Used to recover
    /// the correct casing for VB6 type references (VB6 identifiers are case-insensitive, so
    /// a form/class may be declared as "frmclientesmain" but referenced as "frmClientesMain").
    /// </summary>
    public static ITypeSymbol FindTypeByName(SemanticModel sem, string name, INamespaceSymbol nss = null)
    {
        return FindTypesByName(sem, name, nss).FirstOrDefault();
    }

    public static SyntaxTokenList Modifiers(
        bool isPublic = false, bool isInternal = false, bool isProtected = false,
        bool isStatic = false,
        bool isReadOnly = false, bool isPartial = false)
    {
        IEnumerable<SyntaxKind> GetKinds()
        {
            if (isPublic) yield return SyntaxKind.PublicKeyword;
            if (isInternal) yield return SyntaxKind.InternalKeyword;
            if (isProtected) yield return SyntaxKind.ProtectedKeyword;
            if (isStatic) yield return SyntaxKind.StaticKeyword;
            if (isReadOnly) yield return SyntaxKind.ReadOnlyKeyword;
            if (isPartial) yield return SyntaxKind.PartialKeyword;
        }

        return TokenList(GetKinds().Select(Token));
    }

    public static VariableDeclarationSyntax VariableDeclaration(TypeSyntax type, SyntaxToken name, ExpressionSyntax initializer = null)
        => SyntaxFactory.VariableDeclaration(type,
            SingletonSeparatedList(
                VariableDeclarator(name, null,
                    initializer != null ? EqualsValueClause(initializer) : null
                )
            )
        );

    public static NameSyntax ToName(this ExpressionSyntax expr)
    {
        if (expr is NameSyntax name) {
            return name;
        }
        else if (expr is MemberAccessExpressionSyntax member) {
            var obj = ToName(member.Expression);
            var target = member.Name;
            return QualifiedName(obj, target);
        }
        else {
            throw new ArgumentException("Expression is not a name");
        }
    }

    public static NameSyntax AppendName(this ExpressionSyntax left, ExpressionSyntax right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftName = left.ToName();
        var rightName = right.ToName();

        var current = leftName;
        foreach (var id in right.DescendantNodesAndSelf().OfType<SimpleNameSyntax>()) {
            current = QualifiedName(current, id);
        }

        return current;
    }

    public static IEnumerable<SimpleNameSyntax> EnumerateNames(this ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax inner) {
            yield return inner.Name;
            foreach (var expr in EnumerateNames(inner.Expression)) {
                yield return expr;
            }
        }
        else if (expression is ElementAccessExpressionSyntax element) {
            foreach (var expr in EnumerateNames(element.Expression)) {
                yield return expr;
            }
        }
        else if (expression is SimpleNameSyntax simple) {
            yield return simple;
        }
    }

    public static ArgumentListSyntax ArgumentList(params ExpressionSyntax[] args)
    {
        if (args is null || args.Length == 0) {
            return SyntaxFactory.ArgumentList();
        }
        else if (args.Length == 1) {
            return SyntaxFactory.ArgumentList(SingletonSeparatedList(Argument(args[0])));
        }
        else {
            return SyntaxFactory.ArgumentList(SeparatedList<ArgumentSyntax>(
                new SyntaxNodeOrTokenList(args
                    .Select(a => (SyntaxNodeOrToken)Argument(a))
                    .Intersperse(Token(SyntaxKind.CommaToken)))));
        }
    }

    public static ArgumentListSyntax ArgumentList(params ArgumentSyntax[] args)
    {
        if (args is null || args.Length == 0) {
            return SyntaxFactory.ArgumentList();
        }
        else if (args.Length == 1) {
            return SyntaxFactory.ArgumentList(SingletonSeparatedList(args[0]));
        }
        else {
            return SyntaxFactory.ArgumentList(SeparatedList<ArgumentSyntax>(
                [.. args
                    .Select(a => (SyntaxNodeOrToken)a)
                    .Intersperse(Token(SyntaxKind.CommaToken))]));
        }
    }

    public static StatementSyntax GetBlock(StatementSyntax[] statements, bool allowCollapse)
    {
        if (allowCollapse) {
            if (statements is null || statements.Length == 0) {
                return EmptyStatement();
            }
            else if (statements.Length == 1) {
                return statements[0];
            }
            else {
                return Block(statements);
            }
        }
        else {
            return Block(statements ?? []);
        }
    }

    public static TypeSyntax ToTypeSyntax(this TypeInfo typeInfo)
    {
        var t = typeInfo.ConvertedType;
        if (t?.TypeKind == TypeKind.Dynamic)
            return IdentifierName("dynamic");
        var typeName = t?.ToString();
        return !string.IsNullOrEmpty(typeName)
            ? ParseTypeName(typeName)
            : PredefinedType(Token(SyntaxKind.ObjectKeyword));
    }

    public static TypeSyntax ToTypeSyntax(this ITypeSymbol typeSymbol)
    {
        if (typeSymbol?.TypeKind == TypeKind.Dynamic)
            return IdentifierName("dynamic");
        if (typeSymbol is IArrayTypeSymbol arrayType)
            return ArrayType(arrayType.ElementType.ToTypeSyntax(),
                SingletonList(ArrayRankSpecifier(
                    SeparatedList<ExpressionSyntax>(
                        Enumerable.Repeat(OmittedArraySizeExpression(), arrayType.Rank)))));
        var typeName = typeSymbol?.ToString();
        return !string.IsNullOrEmpty(typeName)
            ? ParseTypeName(typeName)
            : PredefinedType(Token(SyntaxKind.ObjectKeyword));
    }

    static readonly SymbolDisplayFormat NamespaceQualifiedFormat = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static NameSyntax ToNameSyntax(this ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
            return ToNameSyntax(arrayType.ElementType);

        return ParseName(typeSymbol?.ToDisplayString(NamespaceQualifiedFormat));
    }

    public static bool IsEquivalentSyntax(object oldValue, object newValue)
    {
        if (oldValue is null != newValue is null)
            return false;

        if (oldValue is SyntaxNode oldSn && newValue is SyntaxNode newSn) {
            oldSn = oldSn.NormalizeWhitespace();
            newSn = newSn.NormalizeWhitespace();
            return oldSn.IsEquivalentTo(newSn);
        }
        else if (oldValue is SyntaxToken oldToken && newValue is SyntaxToken newToken) {
            oldToken = oldToken.NormalizeWhitespace();
            newToken = newToken.NormalizeWhitespace();
            return oldToken.IsEquivalentTo(newToken);
        }
        else if (oldValue is IReadOnlyList<SyntaxNode> oldList && newValue is IReadOnlyList<SyntaxNode> newList) {
            if (oldList.Count != newList.Count) {
                return false;
            }
            for (int i = 0; i < oldList.Count; i++) {
                if (!IsEquivalentSyntax(oldList[i], newList[i])) {
                    return false;
                }
            }
            return true;
        }
        else if (oldValue is IReadOnlyList<SyntaxToken> oldTokenList && newValue is IReadOnlyList<SyntaxToken> newTokenList) {
            if (oldTokenList.Count != newTokenList.Count) {
                return false;
            }
            for (int i = 0; i < oldTokenList.Count; i++) {
                if (!IsEquivalentSyntax(oldTokenList[i], newTokenList[i])) {
                    return false;
                }
            }
            return true;
        }
        else {
            return string.Equals(oldValue.ToString(), newValue.ToString());
        }
    }

    public static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(this ITypeSymbol type)
    {
        var current = type;
        while (current != null) {
            yield return current;
            current = current.BaseType;
        }
    }

    public static T FirstDescendantOrSelf<T>(this SyntaxNode node) where T : SyntaxNode
    {
        return node.DescendantNodesAndSelf(i => i is not T).OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Walks the parent chain of <paramref name="node"/> looking for syntax contexts
    /// that constrain the expected type: variable declaration, assignment LHS, enclosing
    /// return type, or method-call parameter type.
    /// </summary>
    public static ITypeSymbol TryGetContextualType(this SemanticModel sem, NameSyntax node)
    {
        for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent) {
            switch (ancestor) {
                // Variable declaration where this node IS the declared type:
                //   Widget x = expr  →  infer from initializer expression
                case VariableDeclarationSyntax decl when decl.Type.Span.Contains(node.Span):
                    foreach (var variable in decl.Variables) {
                        if (variable.Initializer?.Value is { } init) {
                            var t = sem.GetTypeInfo(init).Type;
                            if (t is not null) return t;
                        }
                    }
                    return null;

                // Variable declaration where this node is in the initializer:
                //   A.Widget x = new Widget()  →  infer from the declared type
                case VariableDeclarationSyntax decl:
                    return sem.GetTypeInfo(decl.Type).Type;

                // Assignment RHS:  x = new Widget()  →  infer from LHS
                case AssignmentExpressionSyntax assign when assign.Right.Span.Contains(node.Span):
                    return sem.GetTypeInfo(assign.Left).Type;

                // Return statement:  return new Widget()  →  enclosing method/property return type
                case ReturnStatementSyntax:
                    return sem.GetEnclosingSymbol(node.SpanStart) switch {
                        IMethodSymbol   m => m.ReturnType,
                        IPropertySymbol p => p.Type,
                        _ => null
                    };

                // Method argument:  Foo(new Widget())  →  matching parameter type
                case ArgumentSyntax arg
                    when arg.Parent is ArgumentListSyntax argList
                      && argList.Parent is InvocationExpressionSyntax invoc: {
                    var si = sem.GetSymbolInfo(invoc);
                    if ((si.Symbol ?? si.CandidateSymbols.FirstOrDefault()) is IMethodSymbol method) {
                        int index = argList.Arguments.IndexOf(arg);
                        IParameterSymbol param = arg.NameColon is { } nc
                            ? method.Parameters.FirstOrDefault(p => p.Name == nc.Name.Identifier.Text)
                            : index >= 0 && index < method.Parameters.Length ? method.Parameters[index] : null;
                        if (param is not null) return param.Type;
                    }
                    return null;
                }

                // Stop at class / compilation-unit boundaries
                case MemberDeclarationSyntax:
                case CompilationUnitSyntax:
                    return null;
            }
        }
        return null;
    }
}
