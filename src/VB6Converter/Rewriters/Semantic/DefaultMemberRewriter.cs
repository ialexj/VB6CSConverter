using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Expands VB6 implicit default-property usages. In VB6, an object with a default
/// property (DISPID 0) can be used directly wherever the property value is expected:
/// <code>
/// chkArtigos = vbChecked        ' Let chkArtigos._Default = vbChecked
/// If chkArtigos = vbChecked Then ' If chkArtigos._Default = vbChecked Then
/// </code>
/// The initial conversion emits the object reference as-is, which fails to compile once
/// it interacts with a value type, enum, or string (e.g.
/// <c>Operator '==' cannot be applied to operands of type 'CheckBox' and 'CheckBoxConstants'</c>).
/// This rewriter finds the type's <see cref="System.Reflection.DefaultMemberAttribute"/>
/// (emitted by <c>ComStubGenerator</c> for COM default properties, or present on hand-written
/// compatibility shims such as <c>VB.CheckBox</c>) and rewrites the bare object reference to
/// <c>obj.MemberName</c> wherever it is compared/assigned against a value type, enum, or string.
///
/// The rewrite deliberately does not restrict itself to the default member's own declared
/// type (e.g. <c>VB.CheckBox._Default</c> is <c>short</c> but is typically compared against a
/// <c>CheckBoxConstants</c> enum) — a later pass (<see cref="EnumToNumberCastRewriter"/>) adds
/// the necessary cast once the true member type is visible.
///
/// Only reference-type "Set" semantics are excluded: if the other operand is itself a
/// reference type (not string), the expression is left untouched, since VB6's Set-style
/// object assignment/comparison is expected to remain a plain object reference in C#.
/// </summary>
public class DefaultMemberRewriter(SemanticModel semantics) : LoggedRewriter
{
    public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Rewrite(node, node =>
        {
            if (!node.IsKind(SyntaxKind.SimpleAssignmentExpression)) {
                return base.VisitAssignmentExpression(node);
            }

            var leftType = semantics.GetTypeInfo(node.Left).Type;
            var rightType = semantics.GetTypeInfo(node.Right).Type;

            HasUsableDefaultMember(leftType, out var leftMemberName);
            HasUsableDefaultMember(rightType, out var rightMemberName);

            var expandLeft = leftMemberName is not null && IsValueEnumOrString(rightType);
            var expandRight = !expandLeft && rightMemberName is not null && IsValueEnumOrString(leftType);

            if (!expandLeft && !expandRight) {
                return base.VisitAssignmentExpression(node);
            }

            // Visit each side exactly once: either through ExpandDefaultMember (which
            // recurses into the original expression itself before wrapping it) or via a
            // plain Visit. Never fall through to base.VisitAssignmentExpression afterward —
            // that would re-traverse the already-replaced side, and any nested
            // binary/assignment expression inside it would no longer belong to the tree the
            // SemanticModel was created for, causing GetTypeInfo/GetSymbolInfo to throw.
            var newLeft = expandLeft ? ExpandDefaultMember(node.Left, leftMemberName) : (ExpressionSyntax)Visit(node.Left);
            var newRight = expandRight ? ExpandDefaultMember(node.Right, rightMemberName) : (ExpressionSyntax)Visit(node.Right);

            return node.WithLeft(newLeft).WithRight(newRight);
        });

    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        => Rewrite(node, node =>
        {
            var leftType = semantics.GetTypeInfo(node.Left).Type;
            var rightType = semantics.GetTypeInfo(node.Right).Type;

            HasUsableDefaultMember(leftType, out var leftMemberName);
            HasUsableDefaultMember(rightType, out var rightMemberName);

            var expandLeft = leftMemberName is not null && IsValueEnumOrString(rightType);
            var expandRight = rightMemberName is not null && IsValueEnumOrString(leftType);

            if (!expandLeft && !expandRight) {
                return base.VisitBinaryExpression(node);
            }

            // See the comment in VisitAssignmentExpression: visit each side exactly once and
            // never fall through to base.VisitBinaryExpression afterward.
            var newLeft = expandLeft ? ExpandDefaultMember(node.Left, leftMemberName) : (ExpressionSyntax)Visit(node.Left);
            var newRight = expandRight ? ExpandDefaultMember(node.Right, rightMemberName) : (ExpressionSyntax)Visit(node.Right);

            return node.WithLeft(newLeft).WithRight(newRight);
        });

    public override SyntaxNode VisitEqualsValueClause(EqualsValueClauseSyntax node)
        => Rewrite(node, node =>
        {
            var valueType = semantics.GetTypeInfo(node.Value).Type;
            if (!HasUsableDefaultMember(valueType, out var memberName)) {
                return base.VisitEqualsValueClause(node);
            }

            var targetType = GetEqualsValueTargetType(node);
            if (!IsValueEnumOrString(targetType)) {
                return base.VisitEqualsValueClause(node);
            }

            return node.WithValue((ExpressionSyntax)ExpandDefaultMember(node.Value, memberName));
        });

    private ITypeSymbol GetEqualsValueTargetType(EqualsValueClauseSyntax node)
        => node.Parent switch
        {
            VariableDeclaratorSyntax declarator => semantics.GetDeclaredSymbol(declarator) switch
            {
                ILocalSymbol local => local.Type,
                IFieldSymbol field => field.Type,
                _ => null
            },
            ParameterSyntax parameter => (semantics.GetDeclaredSymbol(parameter) as IParameterSymbol)?.Type,
            PropertyDeclarationSyntax property => (semantics.GetDeclaredSymbol(property) as IPropertySymbol)?.Type,
            _ => null
        };

    private ExpressionSyntax ExpandDefaultMember(ExpressionSyntax expression, string memberName)
    {
        var visited = (ExpressionSyntax)Visit(expression);

        // Move the trailing trivia (e.g. whitespace before an operator) from the
        // original expression onto the new final token, so formatting is preserved.
        var trailingTrivia = visited.GetTrailingTrivia();
        visited = visited.WithTrailingTrivia();

        if (NeedsParens(visited)) {
            visited = ParenthesizedExpression(visited);
        }

        var nameNode = IdentifierName(memberName).WithTrailingTrivia(trailingTrivia);

        return MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, visited, nameNode);
    }

    private static bool NeedsParens(ExpressionSyntax expression)
        => expression is BinaryExpressionSyntax
            or ConditionalExpressionSyntax
            or AssignmentExpressionSyntax
            or CastExpressionSyntax
            or LambdaExpressionSyntax
            or AwaitExpressionSyntax
            or RangeExpressionSyntax
            or IsPatternExpressionSyntax
            or SwitchExpressionSyntax;

    /// <summary>
    /// Determines whether <paramref name="type"/> exposes a usable DISPID-0 default
    /// property/field discoverable via <see cref="System.Reflection.DefaultMemberAttribute"/>.
    /// Indexer-backed default members are skipped: those already receive the attribute
    /// implicitly from the C# compiler and are handled elsewhere (element access), not by
    /// this rewrite.
    /// </summary>
    private static bool HasUsableDefaultMember(ITypeSymbol type, out string memberName)
    {
        memberName = null;

        if (!TryGetDefaultMemberName(type, out var name)) {
            return false;
        }

        if (!TryResolveMember(type, name, out _)) {
            return false;
        }

        memberName = name;
        return true;
    }

    private static bool TryGetDefaultMemberName(ITypeSymbol type, out string memberName)
    {
        memberName = null;

        if (type is null) {
            return false;
        }

        foreach (var candidate in GetTypeAndInterfaces(type)) {
            var attribute = candidate.GetAttributes().FirstOrDefault(IsDefaultMemberAttribute);
            if (attribute is not null && attribute.ConstructorArguments is [{ Value: string name }, ..]) {
                memberName = name;
                return true;
            }
        }

        return false;
    }

    private static bool IsDefaultMemberAttribute(AttributeData attribute)
        => attribute.AttributeClass is { Name: "DefaultMemberAttribute" } attributeClass
            && attributeClass.ContainingNamespace?.ToDisplayString() == "System.Reflection";

    private static System.Collections.Generic.IEnumerable<ITypeSymbol> GetTypeAndInterfaces(ITypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType) {
            yield return current;
        }

        foreach (var iface in type.AllInterfaces) {
            yield return iface;
        }
    }

    private static bool TryResolveMember(ITypeSymbol type, string name, out ISymbol member)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType) {
            var candidate = current.GetMembers(name)
                .FirstOrDefault(m => m is IFieldSymbol || (m is IPropertySymbol property && !property.IsIndexer));

            if (candidate is not null) {
                member = candidate;
                return true;
            }
        }

        member = null;
        return false;
    }

    private static bool IsValueEnumOrString(ITypeSymbol type)
        => type is not null
            && (type.IsValueType || type.TypeKind == TypeKind.Enum || type.SpecialType == SpecialType.System_String);
}
