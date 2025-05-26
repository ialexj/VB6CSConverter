using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Converter.Conversion.LoopConverter;
using static VB6Converter.RoslynHelpers;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class StatementConverter
{
    public static BlockSyntax GetBlock(IBlockContext block, CallContext ctx) => (BlockSyntax)GetBlock(block, ctx, false, GetMethodStatements);

    public static StatementSyntax GetBlock(IBlockContext block, CallContext ctx, bool allowCollapse) => GetBlock(block, ctx, allowCollapse, GetMethodStatements);

    static StatementSyntax GetBlock(IBlockContext block, CallContext ctx, bool allowCollapse, Func<IEnumerable<BlockStmtContext>, CallContext, IEnumerable<StatementSyntax>> statementFactory)
    {
        if (block != null) {
            return RoslynHelpers.GetBlock([.. statementFactory(block?.blockStmt(), ctx)], allowCollapse);
        }
        else {
            return Block();
        }
    }
        

    public static IEnumerable<StatementSyntax> GetMethodStatements(IEnumerable<BlockStmtContext> statements, CallContext ctx)
    {
        SyntaxToken? currentLabel = null;

        foreach (var stmt in statements) {
            if (stmt.lineLabel() is LineLabelContext label) {
                currentLabel = GetIdentifier(label.ambiguousIdentifier());
            }
            else {
                foreach (var s in GetMethodStatements(stmt, ctx)) {
                    if (currentLabel is SyntaxToken l) {
                        currentLabel = null;
                        yield return LabeledStatement(l, s);
                    }
                    else {
                        yield return s;
                    }
                }
            }
        }

        static IEnumerable<StatementSyntax> GetMethodStatements(BlockStmtContext stmt, CallContext ctx)
        {
            using var _ = new TraceMethod(stmt);

            try {
                if (stmt.constStmt() is ConstStmtContext @const) {
                    return DeclarationConverter.GetConstantDeclarations(@const).Select(LocalDeclarationStatement);
                }
                else if (stmt.variableStmt() is VariableStmtContext var) {
                    return DeclarationConverter.GetVariableDeclarations(var, true).Select(LocalDeclarationStatement);
                }
                else if (stmt.eraseStmt() is EraseStmtContext erase) {
                    return GetErase(erase, ctx);
                }
                else if (stmt.attributeStmt() is AttributeStmtContext attribute) {
                    return []; // nothing
                }
                else if (stmt.sendkeysStmt() is SendkeysStmtContext sendKeys) {
                    var sendExpr = QualifiedName(IdentifierName("SendKeys"), IdentifierName("Send"));
                    return sendKeys.valueStmt().Select(send => {
                        var value = GetValue(send, ctx);
                        return ExpressionStatement(
                            InvocationExpression(sendExpr, ArgumentList(value))
                        );
                    });
                }
                else {
                    return [ GetMethodStatement(stmt, ctx) ];
                }
            }
            catch (TransformException nse) {
                return [
                    EmptyStatement()
                        .WithError(TransformError.Create(stmt, nse.Message, stmt.GetText()))
                        .WithTrailingTrivia(Whitespace(Environment.NewLine))
                ];
            }
        }

        static StatementSyntax GetMethodStatement(BlockStmtContext stmt, CallContext ctx)
        {
            if (stmt.redimStmt() is RedimStmtContext redim) {
                return GetRedim(redim, ctx);
            }

            else if (stmt.call() is ICallContext call) {
                return GetCall(call, ctx);
            }
            else if (stmt.assignment() is IAssignmentContext assignment) {
                return ExpressionStatement(GetAssignment(assignment, ctx));
            }
            else if (stmt.withStmt() is WithStmtContext with) {
                return GetWith(with, ctx);
            }

            else if (stmt.ifThenElseStmt() is IfThenElseStmtContext ifthen) {
                return GetIf(ifthen, ctx);
            }
            else if (stmt.selectCaseStmt() is SelectCaseStmtContext select) {
                return GetSelectCase(select, ctx);
            }

            else if (GetLoopMethodStatement(stmt, ctx) is StatementSyntax loop) {
                return loop;
            }

            else if (stmt.loadStmt() is LoadStmtContext load) {
                return EmptyStatement().WithTrailingTrivia(Comment($"// {load.GetText()}"));
            }
            else if (stmt.unloadStmt() is UnloadStmtContext unload) {
                if (unload.valueStmt().GetText() == "Me") {
                    return ExpressionStatement(InvocationExpression(IdentifierName("Close"), ArgumentList()));
                }
                else {
                    return EmptyStatement().WithTrailingTrivia(Comment($"// {unload.GetText()}"));
                }
            }

            else if (stmt.openStmt() is OpenStmtContext open) {
                return GetOpen(open, ctx);
            }
            else if (stmt.printStmt() is PrintStmtContext print) {
                return GetPrint(print, ctx);
            }
            else if (stmt.closeStmt() is CloseStmtContext close) {
                return GetClose(close, ctx);
            }
            else if (stmt.lineInputStmt() is LineInputStmtContext lineInput) {
                return ExpressionStatement(InvocationExpression(
                    ParseExpression("FileSystem.LineInput"),
                    ArgumentList(
                        GetValue(lineInput.valueStmt(0), ctx),
                        GetValue(lineInput.valueStmt(1), ctx))));
            }
            else if (stmt.writeStmt() is WriteStmtContext writeStmt) {
                // todo
                return ExpressionStatement(InvocationExpression(
                    ParseExpression("FileSystem.Write"),
                    ArgumentList(
                        Argument(GetValue(writeStmt.valueStmt(), ctx)))));
            }


            else if (stmt.killStmt() is KillStmtContext kill) {
                return GetKill(kill, ctx);
            }

            else if (stmt.raiseEventStmt() is RaiseEventStmtContext raise) {
                var name = GetIdentifierName(raise.ambiguousIdentifier());
                var args = raise.argsCall();

                var statement = ParseStatement($"{name}?.Invoke(this, EventArgs.Empty);");

                if (args != null && args.ChildCount > 0) {
                    statement = statement.WithError(TransformError.Create(raise, "RaiseEvent with arguments not supported"));
                }

                return statement;
            }

            else if (stmt.goToStmt() is GoToStmtContext goTo) {
                return GetGoTo(goTo, ctx);
            }
            else if (stmt.resumeStmt() is ResumeStmtContext resume) {
                return GetResume(resume);
            }
            else if (stmt.onErrorStmt() is OnErrorStmtContext onerror) {
                var comment = EmptyStatement().WithTrailingTrivia(TriviaList(Comment($"// {onerror.GetText()}")));
                if (onerror.GOTO() is not null) {
                    comment = comment.WithAdditionalAnnotations(
                        new SyntaxAnnotation("OnErrorGoto", onerror.valueStmt().GetText()));
                }

                return comment;
            }

            else if (stmt.exitStmt() is ExitStmtContext exit) {
                return GetExit(exit);
            }
            else if (stmt.endStmt() is EndStmtContext end) {
                return GetEnd(end);
            }

            else if (stmt.beepStmt() is BeepStmtContext beepCtx) {
                return ParseStatement("Console.Beep();");
            }

            else {
                throw new TransformException(stmt, "Unknown statement");
            }
        }
    }




    public static AssignmentExpressionSyntax GetAssignment(IAssignmentContext assignment, CallContext ctx)
    {
        using var _ = new TraceMethod(assignment);


        var identifier = GetCallIdentifierExpression(assignment.implicitCallStmt_InStmt(), ctx, true);
        var value = GetValue(assignment.valueStmt(), ctx);
        return AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, identifier, value);
    }


    public static StatementSyntax GetWith(WithStmtContext with, CallContext ctx)
    {
        using var _ = new TraceMethod(with);
        return GetBlock(with.block(), new CallContext(with.implicitCallStmt_InStmt()), true);
    }

    public static ExpressionStatementSyntax GetCall(ICallContext call, CallContext ctx)
    {
        using var _ = new TraceMethod(call);
        var expression = GetCallInvocationExpression(call, ctx);
        return ExpressionStatement(expression);
    }


    public static StatementSyntax GetIf(IfThenElseStmtContext ifthen, CallContext ctx)
    {
        using var _ = new TraceMethod(ifthen);

        if (ifthen is BlockIfThenElseContext @if) {
            IfStatementSyntax current = null;
            if (@if.ifBlockStmt() is IfBlockStmtContext ifBlock) {
                var condition = GetValue(ifBlock.ifConditionStmt().valueStmt(), ctx);
                var then = GetBlock(ifBlock.block(), ctx);
                current = IfStatement(condition, then);
            }

            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var condition = GetValue(elseif.ifConditionStmt().valueStmt(), ctx);
                    var then = GetBlock(elseif.block(), ctx);

                    var next = IfStatement(condition, then);
                    current = current.WithElse(ElseClause(next));
                    current = next;
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                var block = GetBlock(@else.block(), ctx);
                current = current.WithElse(ElseClause(block));
            }

            return (IfStatementSyntax)current.AncestorsAndSelf().Last();
        }
        else if (ifthen is InlineIfThenElseContext inline && inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
            var condition = GetValue(ifcond.valueStmt(), ctx);
            var block = GetBlock(inline.ifInlineBlockStmt(0), ctx, true);

            var current = IfStatement(condition, block);

            if (inline.ifInlineBlockStmt(1) is IBlockContext elseBlock) {
                var elseStatement = GetBlock(elseBlock, ctx, true);
                current = current.WithElse(ElseClause(elseStatement));
            }

            return current;
        }
        else {
            throw new TransformException(ifthen, "Unknown if statement");
        }
    }

    public static StatementSyntax GetSelectCase(SelectCaseStmtContext select, CallContext ctx)
    {
        var condition = GetValue(select.valueStmt(), ctx);

        IEnumerable<SwitchSectionSyntax> GetSections()
        {
            foreach (var caseStmt in select.sC_Case()) {
                var block = GetBlock(caseStmt.block(), ctx).AddStatements(BreakStatement());

                if (caseStmt.sC_Cond() is CaseCondExprContext expr) {
                    var labels = List(GetLabels(expr).ToArray());
                    yield return SwitchSection(labels, block.Statements);
                }
                else if (caseStmt.sC_Cond() is CaseCondElseContext @else) {
                    var labels = SingletonList<SwitchLabelSyntax>(DefaultSwitchLabel());
                    yield return SwitchSection(labels, block.Statements);
                }
                else {
                    throw new TransformException(caseStmt, "Unknown case arm");
                }
            }
        }
        ;

        IEnumerable<SwitchLabelSyntax> GetLabels(CaseCondExprContext cond)
        {
            foreach (var c in cond.sC_CondExpr()) {
                if (c is CaseCondExprValueContext valueCond) {
                    var value = GetValue(valueCond.valueStmt(), ctx);
                    yield return CaseSwitchLabel(value);
                }
                else if (c is CaseCondExprIsContext isCond) {
                    throw new TransformException(isCond, "Can't translate IS condition.");
                }
                else if (c is CaseCondExprToContext toCond) {
                    throw new TransformException(toCond, "Can't translate TO condition.");
                }
                else {
                    throw new TransformException(c, "Unknown case condition");
                }
            }
        }

        try {
            return SwitchStatement(condition, List(GetSections()));
        }
        catch (TransformException) {
            return SelectCaseAsIf(select, ctx);
        }
    }

    public static StatementSyntax SelectCaseAsIf(SelectCaseStmtContext select, CallContext ctx)
    {
        var condition = GetValue(select.valueStmt(), ctx);

        List<IfStatementSyntax> clauses = [];
        ElseClauseSyntax @else = null;

        foreach (var caseStmt in select.sC_Case()) {
            var block = GetBlock(caseStmt.block(), ctx);

            if (caseStmt.sC_Cond() is CaseCondExprContext expr) {
                var labels = List(expr.sC_CondExpr().Select(c => GetCondition(condition, c)));
                foreach (var label in labels) {
                    clauses.Add(IfStatement(label, block));
                }
            }
            else if (caseStmt.sC_Cond() is CaseCondElseContext caseElse) {
                @else = ElseClause(block);
            }
            else {
                throw new TransformException(caseStmt, "Unknown case arm");
            }
        }

        ExpressionSyntax GetCondition(ExpressionSyntax condition, SC_CondExprContext c)
        {
            switch (c) {
                case CaseCondExprValueContext valueCond:
                    var value = GetValue(valueCond.valueStmt(), ctx);
                    return BinaryExpression(SyntaxKind.EqualsExpression, condition, value);


                case CaseCondExprToContext toCond:
                    var min = GetValue(toCond.valueStmt(0), ctx);
                    var max = GetValue(toCond.valueStmt(1), ctx);

                    return BinaryExpression(SyntaxKind.LogicalAndExpression,
                        BinaryExpression(SyntaxKind.GreaterThanOrEqualExpression, condition, min),
                        BinaryExpression(SyntaxKind.LessThanOrEqualExpression, condition, max));

                case CaseCondExprIsContext isCond:
                    var comparison = isCond.comparisonOperator();
                    var kind = ((ITerminalNode)comparison.GetChild(0)).Symbol.Type switch {
                        LT => SyntaxKind.LessThanExpression,
                        LEQ => SyntaxKind.LessThanOrEqualExpression,
                        GT => SyntaxKind.GreaterThanExpression,
                        GEQ => SyntaxKind.GreaterThanOrEqualExpression,
                        EQ => SyntaxKind.EqualsExpression,
                        NEQ => SyntaxKind.NotEqualsExpression,
                        IS => throw new TransformException(comparison, "Not supported 'IS' condition."),
                        LIKE => throw new TransformException(comparison, "Not supported 'LIKE' case condition"),
                        _ => throw new TransformException(comparison, "Unknown case condition")
                    };

                    var v = GetValue(isCond.valueStmt(), ctx);
                    return BinaryExpression(kind, condition, v);

                default:
                    throw new TransformException(c, "Not supported case arm type");
            }
        }


        if (clauses.Count == 1) {
            return @else != null ? clauses[0].WithElse(@else) : (StatementSyntax)clauses[0];
        }
        else {
            if (@else != null) {
                clauses[^1] = clauses[^1].WithElse(@else);
            }

            for (int i = clauses.Count - 1; i > 0; i--) {
                clauses[i - 1] = clauses[i - 1].WithElse(ElseClause(clauses[i]));
            }

            return clauses[0];
        }
    }

    


    public static StatementSyntax GetRedim(RedimStmtContext redim, CallContext ctx)
    {
        using var _ = new TraceMethod(redim);

        var statements = redim.redimSubStmt().Select(rd => {
            var variable   = GetCallIdentifierExpression(rd.implicitCallStmt_InStmt(), ctx);
            var type       = rd.asTypeClause().ToTypeSyntax(true);
            var subscripts = rd.subscripts().subscript().Select(s => GetValue(s.valueStmt(0), ctx));
            var arrayType  = ArrayType(type, SingletonList(ArrayRankSpecifier(SeparatedList(subscripts))));

            if (redim.PRESERVE() is not null) {
                throw new TransformException(redim, "Redim Preserve not supported");
            }
            else {
                return AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    variable, ArrayCreationExpression(arrayType));
            }
        }).Select(ExpressionStatement).ToArray();

        if (statements.Length > 1) {
            return Block(statements);
        }
        else {
            return statements[0];
        }
    }

    public static StatementSyntax GetOpen(OpenStmtContext open, CallContext ctx)
    {
        var file = GetValue(open.valueStmt(0), ctx);
        var name = GetValue(open.valueStmt(1), ctx);       

        var variable = name switch {
            IdentifierNameSyntax n => n,
            LiteralExpressionSyntax l => IdentifierName(l.Token.ValueText.Trim('"', '#')),
            _ => throw new TransformException(open.valueStmt(1), "Unknown open type")
        };

        string GetFileAccess()
        {
            if (open.READ_WRITE() != null) {
                return "FileAccess.ReadWrite";
            }
            else if (open.READ() != null) {
                return "FileAccess.Read";
            }
            else if (open.WRITE() != null) {
                return "FileAccess.Write";
            }
            else {
                return "FileAccess.ReadWrite";
            }
        }

        string GetFileShare()
        {
            if (open.LOCK_READ_WRITE() != null) {
                return "FileShare.None";
            }
            else if (open.LOCK_WRITE() != null) {
                return "FileShare.Read";
            }
            else if (open.LOCK_READ() != null) {
                return "FileShare.Write";
            }
            else if (open.SHARED() != null) {
                return "FileShare.ReadWrite";
            }
            else {
                return "FileShare.None";
            }
        }

        return LocalDeclarationStatement(
                VariableDeclaration(
                    IdentifierName(
                        Identifier(TriviaList(), SyntaxKind.VarKeyword, "var", "var", TriviaList())
                    ),
                    SingletonSeparatedList(
                        VariableDeclarator(variable.Identifier)
                            .WithInitializer(EqualsValueClause(
                                ObjectCreationExpression(IdentifierName("StreamWriter"))
                                    .WithArgumentList(ArgumentList(
                                        InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                IdentifierName("File"), IdentifierName("Open")
                                            )
                                        )
                                        .WithArgumentList(ArgumentList(
                                            file,
                                            ParseName("FileMode.Open"),
                                            ParseName(GetFileAccess()),
                                            ParseName(GetFileShare())
                                        ))

                            ))

                    )
                )
            )))
            .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "System.IO"));
    }

    public static StatementSyntax GetPrint(PrintStmtContext print, CallContext ctx)
    {
        using var _ = new TraceMethod(print);

        var target = GetValue(print.valueStmt(), ctx);

        var outputs = print.outputList().outputList_Expression().Select(o => {
            if (o.SPC() is not null) {
                throw new TransformException(o, "Print SPC not supported.");
            }
            if (o.TAB() is not null) {
                throw new TransformException(o, "Print TAB not supported.");
            }

            if (o.valueStmt() is ValueStmtContext value) {
                return GetValue(value, ctx);
            }
            else {
                throw new TransformException(o, "Print without value");
            }
        }).ToArray();

        var statements = outputs.Select(o => ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, target, IdentifierName("Write")))
                    .WithArgumentList(ArgumentList(outputs))))
            .ToArray();

        return RoslynHelpers.GetBlock(statements, true);
    }

    public static IEnumerable<StatementSyntax> GetErase(EraseStmtContext erase, CallContext ctx)
    {
        foreach (var v in erase.valueStmt()) {
            var value = GetValue(v, ctx);

            yield return ExpressionStatement(
                InvocationExpression(
                    ParseExpression("File.Delete"),
                    ArgumentList(value)));
        }
    }
    
    public static StatementSyntax GetClose(CloseStmtContext close, CallContext ctx)
    {
        var value = GetValue(close.valueStmt(0), ctx);

        return ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    value, IdentifierName("Dispose")),
                ArgumentList()))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    public static StatementSyntax GetKill(KillStmtContext kill, CallContext ctx)
    {
        var value = GetValue(kill.valueStmt(), ctx);
        return ExpressionStatement(
            InvocationExpression(
                QualifiedName(
                    IdentifierName("File").WithAdditionalAnnotations(new SyntaxAnnotation("Using", "System.IO")),
                    IdentifierName("Delete")
                ),
                ArgumentList(value)))
            .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "System.IO"));
    }


    public static StatementSyntax GetGoTo(GoToStmtContext goTo, CallContext ctx)
    {
        using var _ = new TraceMethod(goTo);
        var label = GetValue(goTo.valueStmt(), ctx);
        return GotoStatement(SyntaxKind.GotoStatement, label);
    }

    public static StatementSyntax GetResume(ResumeStmtContext resume)
    {
        if (resume.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
            var expr = GetIdentifierName(identifier);
            return GotoStatement(SyntaxKind.GotoStatement, expr);
        }
        else {
            return EmptyStatement().WithTrailingTrivia(TriviaList(Comment($"// {resume.GetText()}")));
        }
    }


    public static StatementSyntax GetExit(ExitStmtContext exit)
    {
        if (exit.EXIT_SUB() is not null || exit.EXIT_FUNCTION() is not null || exit.EXIT_PROPERTY() is not null) {
            return ReturnStatement(); // will be fixed up later
        }
        else if (exit.EXIT_DO() is not null || exit.EXIT_FOR() is not null) {
            return BreakStatement();
        }
        else {
            throw new TransformException(exit, "Unknown exit type");
        }
    }


    public static StatementSyntax GetEnd(EndStmtContext _)
    {
        return ExpressionStatement(
            ParseExpression("Application.Exit()")
        );
    }

    
}
