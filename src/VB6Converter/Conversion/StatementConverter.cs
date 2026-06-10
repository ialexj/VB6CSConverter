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

            if (stmt.constStmt() is ConstStmtContext @const) {
                return DeclarationConverter.GetConstantDeclarations(@const, ctx.Options).Select(LocalDeclarationStatement);
            }
            else if (stmt.variableStmt() is VariableStmtContext var) {
                return DeclarationConverter.GetVariableDeclarations(var, true, ctx.Options).Select(LocalDeclarationStatement);
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
            else if (stmt.inputStmt() is InputStmtContext inputCtx) {
                var fileNum = GetValue(inputCtx.valueStmt(0), ctx);
                return inputCtx.valueStmt().Skip(1).Select(v =>
                    (StatementSyntax)ExpressionStatement(
                        InvocationExpression(
                            IdentifierName("Input"),
                            ArgumentList(
                                Argument(fileNum),
                                Argument(GetValue(v, ctx)).WithRefKindKeyword(Token(SyntaxKind.RefKeyword))))));
            }
            else {
                return [ GetMethodStatement(stmt, ctx) ];
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
                var fileNum = GetValue(lineInput.valueStmt(0), ctx);
                var variable = GetValue(lineInput.valueStmt(1), ctx);
                return ExpressionStatement(
                    AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        variable,
                        InvocationExpression(IdentifierName("LineInput"), ArgumentList(fileNum))));
            }
            else if (stmt.writeStmt() is WriteStmtContext writeStmt) {
                var fileNum = GetValue(writeStmt.valueStmt(), ctx);
                var items = writeStmt.outputList()?.outputList_Expression()
                    .Where(o => o.valueStmt() is not null)
                    .Select(o => GetValue(o.valueStmt(), ctx))
                    .ToArray() ?? [];
                return ExpressionStatement(
                    InvocationExpression(
                        IdentifierName("Write"),
                        ArgumentList(new[] { Argument(fileNum) }.Concat(items.Select(a => Argument(a))).ToArray())));
            }
            else if (stmt.getStmt() is GetStmtContext getCtx) {
                var getVals = getCtx.valueStmt();
                var (getRecord, getVar) = getVals.Length == 3
                    ? ((ExpressionSyntax)GetValue(getVals[1], ctx), GetValue(getVals[2], ctx))
                    : (null, GetValue(getVals[1], ctx));
                var getArgs = new List<ArgumentSyntax> {
                    Argument(GetValue(getVals[0], ctx)),
                    Argument(getVar).WithRefKindKeyword(Token(SyntaxKind.RefKeyword)),
                };
                if (getRecord is not null) getArgs.Add(Argument(getRecord));
                return ExpressionStatement(
                    InvocationExpression(IdentifierName("FileGet"), ArgumentList(getArgs.ToArray())));
            }
            else if (stmt.putStmt() is PutStmtContext putCtx) {
                var putVals = putCtx.valueStmt();
                var (putRecord, putVar) = putVals.Length == 3
                    ? ((ExpressionSyntax)GetValue(putVals[1], ctx), GetValue(putVals[2], ctx))
                    : (null, GetValue(putVals[1], ctx));
                var putArgs = new List<ArgumentSyntax> {
                    Argument(GetValue(putVals[0], ctx)),
                    Argument(putVar),
                };
                if (putRecord is not null) putArgs.Add(Argument(putRecord));
                return ExpressionStatement(
                    InvocationExpression(IdentifierName("FilePut"), ArgumentList(putArgs.ToArray())));
            }
            else if (stmt.seekStmt() is SeekStmtContext seekCtx) {
                return ExpressionStatement(
                    InvocationExpression(IdentifierName("Seek"), ArgumentList(
                        GetValue(seekCtx.valueStmt(0), ctx),
                        GetValue(seekCtx.valueStmt(1), ctx))));
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
                return EmptyStatement()
                    .WithError(TransformError.Create(stmt, "Unknown statement"));
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
        return GetBlock(with.block(), new CallContext(with.implicitCallStmt_InStmt(), ctx.Options), true);
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
            return EmptyStatement()
                .WithError(TransformError.Create(ifthen, "Unknown if statement"));
        }
    }

    public static StatementSyntax GetSelectCase(SelectCaseStmtContext select, CallContext ctx)
    {
        var condition = GetValue(select.valueStmt(), ctx);

        static bool CanBeSwitch(SelectCaseStmtContext select) =>
            select.sC_Case().All(c =>
                c.sC_Cond() is CaseCondElseContext ||
                (c.sC_Cond() is CaseCondExprContext expr &&
                 expr.sC_CondExpr().All(e => e is CaseCondExprValueContext)));

        if (CanBeSwitch(select)) {
            IEnumerable<SwitchSectionSyntax> GetSections()
            {
                foreach (var caseStmt in select.sC_Case()) {
                    var block = GetBlock(caseStmt.block(), ctx).AddStatements(BreakStatement());

                    if (caseStmt.sC_Cond() is CaseCondExprContext expr) {
                        var labels = List(GetLabels(expr).ToArray());
                        yield return SwitchSection(labels, block.Statements);
                    }
                    else if (caseStmt.sC_Cond() is CaseCondElseContext) {
                        yield return SwitchSection(
                            SingletonList<SwitchLabelSyntax>(DefaultSwitchLabel()),
                            block.Statements);
                    }
                    else {
                        yield return SwitchSection(
                            SingletonList<SwitchLabelSyntax>(
                                CaseSwitchLabel(ParseExpression("default")
                                    .WithError(TransformError.Create(caseStmt, "Unknown case arm")))),
                            SingletonList<StatementSyntax>(BreakStatement()));
                    }
                }
            }

            IEnumerable<SwitchLabelSyntax> GetLabels(CaseCondExprContext cond)
            {
                foreach (var c in cond.sC_CondExpr()) {
                    if (c is CaseCondExprValueContext valueCond) {
                        var value = GetValue(valueCond.valueStmt(), ctx);
                        yield return CaseSwitchLabel(value);
                    }
                    else {
                        yield return CaseSwitchLabel(
                            ParseExpression("default")
                                .WithError(TransformError.Create(c, "Unknown case condition")));
                    }
                }
            }

            return SwitchStatement(condition, List(GetSections()));
        }
        else {
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
            else if (caseStmt.sC_Cond() is CaseCondElseContext) {
                @else = ElseClause(block);
            }
            else {
                clauses.Add(IfStatement(
                        LiteralExpression(SyntaxKind.FalseLiteralExpression),
                        Block())
                    .WithError(TransformError.Create(caseStmt, "Unknown case arm")));
            }
        }

        ExpressionSyntax GetCondition(ExpressionSyntax condition, SC_CondExprContext c)
        {
            if (c is CaseCondExprValueContext valueCond) {
                var value = GetValue(valueCond.valueStmt(), ctx);
                return BinaryExpression(SyntaxKind.EqualsExpression, condition, value);
            }
            else if (c is CaseCondExprToContext toCond) {
                var min = GetValue(toCond.valueStmt(0), ctx);
                var max = GetValue(toCond.valueStmt(1), ctx);
                return BinaryExpression(SyntaxKind.LogicalAndExpression,
                    BinaryExpression(SyntaxKind.GreaterThanOrEqualExpression, condition, min),
                    BinaryExpression(SyntaxKind.LessThanOrEqualExpression, condition, max));
            }
            else if (c is CaseCondExprIsContext isCond) {
                var comparison = isCond.comparisonOperator();
                SyntaxKind? kind = ((ITerminalNode)comparison.GetChild(0)).Symbol.Type switch {
                    LT => SyntaxKind.LessThanExpression,
                    LEQ => SyntaxKind.LessThanOrEqualExpression,
                    GT => SyntaxKind.GreaterThanExpression,
                    GEQ => SyntaxKind.GreaterThanOrEqualExpression,
                    EQ => SyntaxKind.EqualsExpression,
                    NEQ => SyntaxKind.NotEqualsExpression,
                    _ => (SyntaxKind?)null
                };

                if (kind is null) {
                    return ParseExpression("false")
                        .WithError(TransformError.Create(comparison, $"Not supported '{comparison.GetText()}' condition"));
                }

                var v = GetValue(isCond.valueStmt(), ctx);
                return BinaryExpression(kind.Value, condition, v);
            }
            else {
                return ParseExpression("false")
                    .WithError(TransformError.Create(c, "Not supported case arm type"));
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

        var statements = redim.redimSubStmt().Select<RedimSubStmtContext, ExpressionSyntax>(rd => {
            var variable   = GetCallIdentifierExpression(rd.implicitCallStmt_InStmt(), ctx);
            var type       = rd.asTypeClause().ToTypeSyntax(true, ctx.Options?.UseDynamic ?? true);
            var subscripts = rd.subscripts().subscript().Select(s => GetValue(s.valueStmt(0), ctx)).ToArray();

            if (redim.PRESERVE() is not null) {
                if (subscripts.Length != 1) {
                    return ParseExpression("default")
                        .WithError(TransformError.Create(rd, "Multi-dimensional Redim Preserve not supported"));
                }

                return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Array"),
                        IdentifierName("Resize")),
                    ArgumentList(
                        Argument(variable).WithRefKindKeyword(Token(SyntaxKind.RefKeyword)),
                        Argument(subscripts[0])));
            }
            else {
                var arrayType = ArrayType(type, SingletonList(ArrayRankSpecifier(SeparatedList(subscripts))));
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
        var path = GetValue(open.valueStmt(0), ctx);
        var fileNum = GetValue(open.valueStmt(1), ctx);

        string GetMode()
        {
            if (open.APPEND() != null) return "OpenMode.Append";
            if (open.BINARY() != null) return "OpenMode.Binary";
            if (open.INPUT() != null) return "OpenMode.Input";
            if (open.OUTPUT() != null) return "OpenMode.Output";
            return "OpenMode.Random";
        }

        string GetAccess()
        {
            if (open.READ_WRITE() != null) return "OpenAccess.ReadWrite";
            if (open.READ() != null) return "OpenAccess.Read";
            if (open.WRITE() != null) return "OpenAccess.Write";
            return null;
        }

        string GetShare()
        {
            if (open.LOCK_READ_WRITE() != null) return "OpenShare.LockReadWrite";
            if (open.LOCK_WRITE() != null) return "OpenShare.LockWrite";
            if (open.LOCK_READ() != null) return "OpenShare.LockRead";
            if (open.SHARED() != null) return "OpenShare.Shared";
            return null;
        }

        var args = new List<ArgumentSyntax> {
            Argument(fileNum),
            Argument(path),
            Argument(ParseName(GetMode()))
        };

        var access = GetAccess();
        var share = GetShare();
        if (access is not null || share is not null) {
            args.Add(Argument(ParseName(access ?? "OpenAccess.Default")));
            if (share is not null) args.Add(Argument(ParseName(share)));
        }

        if (open.LEN() is not null && open.valueStmt().Length > 2) {
            args.Add(Argument(GetValue(open.valueStmt(2), ctx))
                .WithNameColon(NameColon(IdentifierName("RecordLength"))));
        }

        return ExpressionStatement(
            InvocationExpression(
                ParseExpression("Microsoft.VisualBasic.FileSystem.FileOpen"),
                ArgumentList(args.ToArray())));
    }

    public static StatementSyntax GetPrint(PrintStmtContext print, CallContext ctx)
    {
        using var _ = new TraceMethod(print);

        var fileNum = GetValue(print.valueStmt(), ctx);
        var outputList = print.outputList();

        ExpressionSyntax GetOutputExpression(OutputList_ExpressionContext o)
        {
            if (o.SPC() is not null) {
                var spcArgs = o.argsCall();
                if (spcArgs?.argCall().Length > 0) {
                    return InvocationExpression(IdentifierName("SPC"),
                        ArgumentList(GetValue(spcArgs.argCall(0).valueStmt(), ctx)));
                }
                return InvocationExpression(IdentifierName("SPC"), ArgumentList());
            }
            if (o.TAB() is not null) {
                var tabArgs = o.argsCall();
                if (tabArgs?.argCall().Length > 0) {
                    return InvocationExpression(IdentifierName("TAB"),
                        ArgumentList(GetValue(tabArgs.argCall(0).valueStmt(), ctx)));
                }
                return InvocationExpression(IdentifierName("TAB"), ArgumentList());
            }
            if (o.valueStmt() is ValueStmtContext value) {
                return GetValue(value, ctx);
            }
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(""))
                .WithError(TransformError.Create(o, "Print without value"));
        }

        // Trailing semicolon suppresses newline → use Print; otherwise use PrintLine
        var lastSep = outputList?.children?.OfType<ITerminalNode>()
            .LastOrDefault(t => t.GetText() is ";" or ",");
        var methodName = lastSep?.GetText() == ";" ? "Print" : "PrintLine";

        var items = outputList?.outputList_Expression()
            .Select(GetOutputExpression).ToArray() ?? [];

        return ExpressionStatement(
            InvocationExpression(
                IdentifierName(methodName),
                ArgumentList(new[] { Argument(fileNum) }.Concat(items.Select(a => Argument(a))).ToArray())));
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
        var fileNums = close.valueStmt().Select(v => GetValue(v, ctx)).ToArray();
        return ExpressionStatement(
            InvocationExpression(IdentifierName("FileClose"), ArgumentList(fileNums)));
    }

    public static StatementSyntax GetKill(KillStmtContext kill, CallContext ctx)
    {
        var value = GetValue(kill.valueStmt(), ctx);
        return ExpressionStatement(
            InvocationExpression(IdentifierName("Kill"), ArgumentList(value)));
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
            return EmptyStatement()
                .WithError(TransformError.Create(exit, "Unknown exit type"));
        }
    }


    public static StatementSyntax GetEnd(EndStmtContext _)
    {
        return ExpressionStatement(
            ParseExpression("Application.Exit()")
        );
    }


}
