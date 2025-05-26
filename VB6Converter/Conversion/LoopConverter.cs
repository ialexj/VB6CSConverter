using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.StatementConverter;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class LoopConverter
{
    public static StatementSyntax GetLoopMethodStatement(BlockStmtContext stmt, CallContext ctx)
    {
        if (stmt.forNextStmt() is ForNextStmtContext fornext) {
            return GetForNext(fornext, ctx);
        }
        else if (stmt.forEachStmt() is ForEachStmtContext forEach) {
            return GetForEach(forEach, ctx);
        }
        else if (stmt.doLoopStmt() is DoLoopStmtContext doLoop) {
            return GetDoLoop(doLoop, ctx);
        }
        else if (stmt.whileWendStmt() is WhileWendStmtContext whileWend) {
            return GetWhile(whileWend, ctx);
        }
        else {
            return null;
        }
    }

    public static ForStatementSyntax GetForNext(ForNextStmtContext forNext, CallContext ctx)
    {
        var type = forNext.asTypeClause() is AsTypeClauseContext asType
            ? CommonConverter.ToTypeSyntax(asType)
            : PredefinedType(Token(SyntaxKind.IntKeyword));

        var variable = GetIdentifierName(forNext.iCS_S_VariableOrProcedureCall());

        var start = GetValue(forNext.valueStmt(0), ctx);
        var end = GetValue(forNext.valueStmt(1), ctx);
        var step = GetValue(forNext.valueStmt(2), ctx);

        bool stepIsOne = true, stepIsNegative = false;
        if (step is LiteralExpressionSyntax literal && literal.Token.Value is int istep) {
            stepIsOne = istep == 1 || istep == -1;
            stepIsNegative = istep < 0;
            if (stepIsNegative) {
                step = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(-istep));
            }
        }

        var decl = AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, variable, start);

        var ops = stepIsNegative
            ? (SyntaxKind.PostDecrementExpression, SyntaxKind.SubtractAssignmentExpression, SyntaxKind.GreaterThanOrEqualExpression)
            : (SyntaxKind.PostIncrementExpression, SyntaxKind.AddAssignmentExpression, SyntaxKind.LessThanOrEqualExpression);


        ExpressionSyntax incrementor = stepIsOne
            ? PostfixUnaryExpression(ops.Item1, variable)
            : AssignmentExpression(ops.Item2, variable, step);

        var block = GetBlock(forNext.block(), ctx, false);

        return ForStatement(block)
            .WithInitializers(SingletonSeparatedList<ExpressionSyntax>(decl))
            .WithCondition(BinaryExpression(ops.Item3, variable, GetValue(forNext.valueStmt(1), ctx)))
            .WithIncrementors(SingletonSeparatedList(incrementor));
    }

    public static StatementSyntax GetForEach(ForEachStmtContext forEach, CallContext ctx)
    {
        using var _ = new TraceMethod(forEach);

        var variable   = GetIdentifier(forEach.ambiguousIdentifier(0)).WithAdditionalAnnotations(new SyntaxAnnotation("ForEachVariable"));
        var enumerator = GetValue(forEach.valueStmt(), ctx);
        var statements = GetBlock(forEach.block(), ctx, false);

        return ForEachStatement(
            IdentifierName(Identifier(TriviaList(), SyntaxKind.VarKeyword, "var", "var", TriviaList())),
            variable,
            enumerator,
            statements);
    }

    public static StatementSyntax GetDoLoop(DoLoopStmtContext doLoop, CallContext ctx)
    {
        using var _ = new TraceMethod(doLoop);

        var condition = GetValue(doLoop.valueStmt(), ctx);
        var statements = GetBlock(doLoop.block(), ctx);
        return WhileStatement(condition, statements);
    }

    public static StatementSyntax GetWhile(WhileWendStmtContext whileWend, CallContext ctx)
    {
        using var _ = new TraceMethod(whileWend);

        var condition = GetValue(whileWend.valueStmt(), ctx);
        var statements = GetBlock(whileWend.block().First(), ctx);
        return WhileStatement(condition, statements);
    }
}
