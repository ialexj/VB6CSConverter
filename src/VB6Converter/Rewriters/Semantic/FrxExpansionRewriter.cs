using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace VB6Converter.Rewriters.Semantic;

/// <summary>
/// Expands FRX-backed designer assignments (marked by a trailing
/// "// Resource: _Resources/...json" comment) into indexed setter calls.
/// </summary>
public class FrxExpansionRewriter(SemanticModel model) : LoggedRewriter
{
    private const string ResourceMarker = "Resource:";
    private static readonly Dictionary<string, string[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    public override SyntaxNode VisitBlock(BlockSyntax node)
        => Rewrite(node, node => {
            var visited = (BlockSyntax)base.VisitBlock(node);
            var rewritten = new List<StatementSyntax>(visited.Statements.Count);
            bool changed = false;

            foreach (var statement in visited.Statements) {
                if (TryExpand(statement, out var expanded)) {
                    rewritten.AddRange(expanded);
                    changed = true;
                    continue;
                }

                rewritten.Add(statement);
            }

            return changed ? visited.WithStatements(List(rewritten)) : visited;
        });

    private bool TryExpand(StatementSyntax statement, out IReadOnlyList<StatementSyntax> expanded)
    {
        expanded = [];

        if (statement is not ExpressionStatementSyntax {
            Expression: AssignmentExpressionSyntax { Left: ExpressionSyntax left } assignment
        } exprStmt) {
            return false;
        }

        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) {
            return false;
        }

        if (!TryGetResourcePath(exprStmt, out var resourcePath)) {
            return false;
        }

        if (!resourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryGetSetter(left, out var setTarget, out var valueParameterType)) {
            return false;
        }

        if (!TryReadItems(resourcePath, exprStmt.SyntaxTree.FilePath, out var items)) {
            return false;
        }

        var statements = new List<StatementSyntax>(items.Length);
        for (int i = 0; i < items.Length; i++) {
            if (!TryCreateValueExpression(items[i], valueParameterType, out var valueExpr)) {
                return false;
            }

            var invocation = InvocationExpression(
                setTarget,
                ArgumentList(SeparatedList(new[] {
                    Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i))),
                    Argument(valueExpr)
                })));

            statements.Add(ExpressionStatement(invocation));
        }

        expanded = statements;
        return true;
    }

    private bool TryGetSetter(ExpressionSyntax left, out ExpressionSyntax setTarget, out ITypeSymbol valueParameterType)
    {
        setTarget = null;
        valueParameterType = null;

        string propertyName;
        ExpressionSyntax receiver;
        if (left is MemberAccessExpressionSyntax ma) {
            propertyName = ma.Name.Identifier.Text;
            receiver = ma.Expression;
        }
        else {
            return false;
        }

        var setName = "Set" + propertyName;
        var targetType = model.GetTypeInfo(receiver).Type;
        if (targetType is null) {
            return false;
        }

        var setter = targetType.GetMembers(setName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters.Length == 2
                && m.Parameters[0].Type.SpecialType is SpecialType.System_Int16
                    or SpecialType.System_Int32
                    or SpecialType.System_Int64);

        if (setter is null) {
            return false;
        }

        setTarget = MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            IdentifierName(setName));
        valueParameterType = setter.Parameters[1].Type;
        return true;
    }

    private static bool TryGetResourcePath(ExpressionStatementSyntax stmt, out string resourcePath)
    {
        resourcePath = null;
        foreach (var trivia in stmt.SemicolonToken.TrailingTrivia) {
            var text = trivia.ToFullString();
            var idx = text.IndexOf(ResourceMarker, StringComparison.Ordinal);
            if (idx < 0) {
                continue;
            }

            resourcePath = text[(idx + ResourceMarker.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(resourcePath)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadItems(string resourcePath, string sourceFilePath, out string[] items)
    {
        items = [];

        if (string.IsNullOrWhiteSpace(sourceFilePath)) {
            return false;
        }

        var fullPath = ResolveResourcePath(sourceFilePath, resourcePath);
        if (fullPath is null) {
            return false;
        }

        if (Cache.TryGetValue(fullPath, out items)) {
            return true;
        }

        var parsed = JsonSerializer.Deserialize<string[]>(File.ReadAllText(fullPath));
        if (parsed is null) {
            return false;
        }

        items = parsed;
        Cache[fullPath] = parsed;
        return true;
    }

    private static string ResolveResourcePath(string sourceFilePath, string resourcePath)
    {
        var normalizedRelativePath = resourcePath.Replace('/', Path.DirectorySeparatorChar);
        var sourceDir = Path.GetDirectoryName(sourceFilePath);
        if (sourceDir is null) {
            return null;
        }

        var current = new DirectoryInfo(sourceDir);
        while (current is not null) {
            var candidate = Path.Combine(current.FullName, normalizedRelativePath);
            if (File.Exists(candidate)) {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool TryCreateValueExpression(string item, ITypeSymbol type, out ExpressionSyntax value)
    {
        value = null;

        switch (type.SpecialType) {
            case SpecialType.System_String:
            case SpecialType.System_Object:
                value = LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(item));
                return true;

            case SpecialType.System_Int16:
                if (short.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i16)) {
                    value = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i16));
                    return true;
                }
                return false;

            case SpecialType.System_Int32:
                if (int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32)) {
                    value = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i32));
                    return true;
                }
                return false;

            case SpecialType.System_Int64:
                if (long.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64)) {
                    value = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i64));
                    return true;
                }
                return false;

            case SpecialType.System_Boolean:
                if (bool.TryParse(item, out var b)) {
                    value = LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
                    return true;
                }
                return false;

            case SpecialType.System_Double:
                if (double.TryParse(item, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d)) {
                    value = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(d));
                    return true;
                }
                return false;

            default:
                return false;
        }
    }
}
