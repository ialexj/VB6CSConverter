using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using static VisualBasic6Parser;

namespace VB6Converter.CSharpModel;

partial class CSharpModelConverter()
{
    record struct CallContext(
        IReadOnlyDictionary<string, CSharpVariable> Variables,
        ImplicitCallStmt_InStmtContext ImplicitCallee = null,
        CSharpMethod Method = null,
        List<CSharpStatement> CurrentBlock = null,
        CSharpTryBlock TryBlock = null) { }

    Stack<CallContext> _contexts = [];
    Stack<CSharpMethod> _methods = [];
    Stack<ImplicitCallStmt_InStmtContext> _with = [];

    CallContext PushContext(
        IEnumerable<CSharpVariable> vars = null,
        ImplicitCallStmt_InStmtContext callee = null,
        CSharpMethod method = null,
        List<CSharpStatement> block = null,
        CSharpTryBlock tryBlock = null)
    {
        _contexts.TryPeek(out CallContext last);

        var newVars = ImmutableDictionary.CreateRange(last.Variables ?? new Dictionary<string, CSharpVariable>());
        if (vars != null) {
            newVars.SetItems(vars.Select(a => new KeyValuePair<string, CSharpVariable>(a.Name, a)));
        }

        var context = new CallContext(
            newVars, 
            callee ?? last.ImplicitCallee, 
            method ?? last.Method,
            block ?? last.CurrentBlock,
            tryBlock ?? last.TryBlock);

        _contexts.Push(context);
        return context;
    }


    public CSharpClass GetClass(ModuleContext module, string name)
    {
        using var _ = new TraceMethod(module);

        var bodyElements = module.moduleBody().moduleBodyElement();
        
        var c = new CSharpClass(name);

        PushContext();
        try {
            // Collect all declarations first
            foreach (var block in bodyElements.Select(e => e.moduleBlock()).OfType<ModuleBlockContext>()) {
                var statements = GetBlock(block.block());

                foreach (var statement in statements) {
                    c.Members.Add(new CSharpClassStatement(statement));
                    if (statement is CSharpDeclarationStatement decl) {
                        c.Declarations.Add(decl);
                    }
                }
            }
        }
        finally {
            _contexts.Pop();
        }

        PushContext(c.Declarations.Select(d => d.Variable));
        try {
            foreach (var element in bodyElements) {
                if (element.moduleBlock() is not null) {
                    continue; // Already processed
                }

                else if (element.subStmt() is SubStmtContext sub) {
                    c.Members.Add(GetMethod(sub));
                }
                else if (element.functionStmt() is FunctionStmtContext func) {
                    c.Members.Add(GetMethod(func));
                }
                else if (element.enumerationStmt() is EnumerationStmtContext enums) {
                    GetEnum(enums);
                }
                //else if (element.typeStmt() is TypeStmtContext type) {
                //    WriteType(type);
                //}
                //else if (element.declareStmt() is DeclareStmtContext declare) {
                //    WriteDeclare(declare);
                //}
                else {
                    NotSupported(element);
                }
            }
        }
        finally {
            _contexts.Pop();
        }

        return c;
    }

    CSharpEnumStatement GetEnum(EnumerationStmtContext e)
    {
        var enumm = new CSharpEnumStatement();

        if (e.publicPrivateVisibility() is PublicPrivateVisibilityContext v) {
            enumm.Visibility = GetVisibility(v);
        }

        enumm.Name = GetIdentifier(e.ambiguousIdentifier());

        var visiblity = GetVisibility(e.publicPrivateVisibility());

        var constants = e.enumerationStmt_Constant().Select(c => {
            var name = GetIdentifier(c.ambiguousIdentifier());
            var value = c.valueStmt() is ValueStmtContext v ? GetValue(v) : null;
            return (name, value);
        });

        return enumm;
    }
    /*
    CSharpGenericStatement WriteType(TypeStmtContext type)
    {
        if (type.visibility() is VisibilityContext visibility) {
            WriteVisibility(visibility);
        }

        sb.Write(" struct ");
        WriteIdentifier(type.ambiguousIdentifier());
        sb.StartBlock(" {");

        foreach (var t in type.typeStmt_Element()) {
            sb.Write("public ");
            WriteAsTypeClause(t.asTypeClause());
            sb.Write(" ");
            WriteIdentifier(t.ambiguousIdentifier());
            sb.WriteLine(";");
        }

        sb.EndBlock("}");
    }

    CSharpGenericStatement WriteDeclare(DeclareStmtContext declare)
    {
        Write("[DllImport(");
        Write(declare.STRINGLITERAL(0).GetText());
        Write(", EntryPoint = \"");
        WriteIdentifier(declare.ambiguousIdentifier());
        sb.WriteLine("\")]");

        if (declare.visibility() is VisibilityContext visibility) {
            WriteVisibility(visibility);
        }

        Write(" static extern ");

        if (declare.asTypeClause() is AsTypeClauseContext asType) {
            WriteAsTypeClause(asType);
        }
        else {
            Write("void");
        }

        Write(" ");

        if (declare.STRINGLITERAL(1) is ITerminalNode alias) {
            Write(alias.GetText().Trim('"'));
        }
        else {
            WriteIdentifier(declare.ambiguousIdentifier());
        }

        WriteArgList(declare.argList());
        sb.WriteLine(";");
    }
    */


    // Methods

    CSharpMethod GetMethod(SubStmtContext sub)
    {
        using var _ = new TraceMethod(sub);

        var name = GetIdentifier(sub.ambiguousIdentifier());
        var visb = GetVisibility(sub.visibility());
        var args = GetMethodArguments(sub.argList());

        // Combine module vars with method vars
        PushContext(vars: args.Select(a => a.Variable));
        try {
            var method = new CSharpMethod(name) {
                ReturnType = CSharpType.Void,
                Visibility = visb,
                Arguments  = args
            };

            _methods.Push(method);
            method.Statements = GetBlock(sub.block());
            return method;
        }
        finally {
            _methods.Pop();
            _contexts.Pop();
        }
    }

    CSharpMethod GetMethod(FunctionStmtContext func)
    {
        using var _ = new TraceMethod(func);

        var name = GetIdentifier(func.ambiguousIdentifier());
        var visb = GetVisibility(func.visibility());
        var args = GetMethodArguments(func.argList());

        PushContext(args.Select(a => a.Variable));
        try {
            var method = new CSharpMethod(name) {
                ReturnType = GetType(func.asTypeClause()),
                Visibility = visb,
                Arguments  = args
            };

            _methods.Push(method);
            method.Statements = GetBlock(func.block());
            return method;
        }
        finally {
            _methods.Pop();
            _contexts.Pop();
        }
    }

    List<CSharpArgument> GetMethodArguments(ArgListContext argListContext)
    {
        using var _ = new TraceMethod(argListContext);

        CSharpArgument GetArgument(ArgContext arg) => new(
            Variable: new CSharpVariable(
                Name: GetIdentifier(arg.ambiguousIdentifier()),
                Type: GetType(arg.asTypeClause())),
            DefaultValue: arg.argDefaultValue() is ArgDefaultValueContext def ? GetValue(def.valueStmt()) : null
        );

        return argListContext.arg().Select(GetArgument).ToList();
    }



    // Statements

    List<CSharpStatement> GetBlock(BlockContext block)
    {
        using var _ = new TraceMethod(block);

        var list = new List<CSharpStatement>();
        PushContext(block: list);
        try {
            foreach (var stmt in block.blockStmt()) {
                _contexts.Peek().CurrentBlock.AddRange(GetBlockStatements(stmt));
            }
        }
        finally {
            _contexts.Pop();
        }

        return list;
    }

    IEnumerable<CSharpStatement> GetBlockStatements(BlockStmtContext stmt)
    {
        using var _ = new TraceMethod(stmt);

        if (stmt.commentStmt() is CommentStmtContext comment) {
            return [new CSharpGenericStatement($"// {comment.COMMENT()}")];
        }
        else if (stmt.lineLabel() is LineLabelContext label) {
            if (label.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
                var text = GetIdentifier(identifier);

                var context = _contexts.Peek();
                if (context.TryBlock != null && string.Equals(context.TryBlock.CatchLabel, text)) {
                    _contexts.Pop();
                    PushContext(block: context.TryBlock.CatchStatements, tryBlock: context.TryBlock);
                    return [];
                }

                return [new CSharpGenericStatement($"{text}:")];
            }

            return [new CSharpGenericStatement($"{label.GetText()}:")];
        }


        else if (stmt.constStmt() is ConstStmtContext @const) {
            return GetConstants(@const);
        }
        else if (stmt.variableStmt() is VariableStmtContext var) {
            return GetVariables(var);
        }

        


        else if (stmt.implicitCallStmt_InBlock() is ImplicitCallStmt_InBlockContext implicitStmt) {
            return [GetImplicitCall(implicitStmt)];
        }

        else if (stmt.letStmt() is LetStmtContext let) {
            return [GetLet(let)];
        }
        else if (stmt.setStmt() is SetStmtContext set) {
            return [GetSet(set)];
        }

        else if (stmt.ifThenElseStmt() is IfThenElseStmtContext ifthen) {
            return [GetIf(ifthen)];
        }
        else if (stmt.selectCaseStmt() is SelectCaseStmtContext select) {
            return [GetSelect(select)];
        }

        else if (stmt.doLoopStmt() is DoLoopStmtContext doLoop) {
            return [GetDoLoop(doLoop)];
        }
        else if (stmt.forNextStmt() is ForNextStmtContext forNext) {
            return [GetForNext(forNext)];
        }

        else if (stmt.withStmt() is WithStmtContext with) {
            return [GetWith(with)];
        }

        else if (stmt.openStmt() is OpenStmtContext open) {
            return [GetOpen(open)];
        }
        else if (stmt.printStmt() is PrintStmtContext print) {
            return [GetPrint(print)];
        }
        else if (stmt.closeStmt() is CloseStmtContext close) {
            return [GetClose(close)];
        }
        else if (stmt.eraseStmt() is EraseStmtContext erase) {
            return GetErase(erase);
        }

        else if (stmt.exitStmt() is ExitStmtContext exit) {
            return [new CSharpGenericStatement($"return;")];
        }
        else if (stmt.resumeStmt() is ResumeStmtContext resume) {
            return [GetResume(resume)];
        }

        else if (stmt.goToStmt() is GoToStmtContext gotoCtx) {
            return [GetGoto(gotoCtx)];
        }


        else if (stmt.onErrorStmt() is OnErrorStmtContext onerror) {
            return [new CSharpGenericStatement($"// {onerror.GetText()}")];

            //if (onerror.GOTO() != null) {
            //    var block = new CSharpTryBlock() {
            //        CatchLabel = GetValue(onerror.valueStmt()).ToString()
            //    };

            //    PushContext(block: block.TryStatements, tryBlock: block);
            //    return [block];
            //}
        }

        else {
            Trace.TraceWarning("Unknown statement: \r\n{0}", new ConsoleVisitor().Visit(stmt));
            return [new CSharpGenericStatement(stmt.GetText())];
        }
    }

    CSharpGotoStatement GetGoto(GoToStmtContext gotoCtx)
    {
        using var _ = new TraceMethod(gotoCtx);

        return new CSharpGotoStatement() {
            Label = GetValue(gotoCtx.valueStmt())
        };
    }

    IEnumerable<CSharpDeclarationStatement> GetConstants(ConstStmtContext @const)
    {
        using var _ = new TraceMethod(@const);

        var visibility = CSharpVisibility.Private;

        if (@const.publicPrivateGlobalVisibility() is PublicPrivateGlobalVisibilityContext vis) {
            if (vis.PUBLIC() != null || vis.GLOBAL() != null) {
                visibility = CSharpVisibility.Public;
            }
        }

        foreach (var sub in @const.constSubStmt()) {
            var variable = new CSharpVariable(
                Name: GetIdentifier(sub.ambiguousIdentifier()),
                Type: GetType(sub.asTypeClause()));

            var value = GetValue(sub.valueStmt());

            yield return new CSharpDeclarationStatement(variable, value, CSharpDeclarationOption.Const, visibility);
        }
    }

    IEnumerable<CSharpDeclarationStatement> GetVariables(VariableStmtContext var)
    {
        using var _ = new TraceMethod(var);

        var visibility = GetVisibility(var.visibility());
        var option = CSharpDeclarationOption.Default;
        if (var.STATIC() != null) {
            option = CSharpDeclarationOption.Static;
        }

        foreach (var sub in var.variableListStmt().variableSubStmt()) {
            List<string> arrayDimensions = [];

            if (sub.subscripts() is SubscriptsContext subscripts) {
                foreach (var subscript in subscripts.subscript()) {
                    var from = GetValue(subscript.valueStmt(0));
                    var to   = subscript.valueStmt(1) is ValueStmtContext v ? GetValue(v) : null;
                    arrayDimensions.Add($"{from} + 1");
                }
            }

            var variable = new CSharpVariable(
                Name: GetIdentifier(sub.ambiguousIdentifier()),
                Type: GetType(sub.asTypeClause()),
                ArrayDimensions: arrayDimensions.Count);

            CSharpExpression value = null;
            if (arrayDimensions.Count > 0) {
                value = new CSharpExpression("new " + variable.Type + "[" + string.Join(", ", arrayDimensions) + "]");
            }

            yield return new CSharpDeclarationStatement(variable, value, option, visibility);
        }
    }

    ICSharpExpression GetValue(ValueStmtContext value)
    {
        using var _ = new TraceMethod(value);

        var context = _contexts.Peek();

        if (value is VsICSContext vsics) {
            return GetImplicitCall(vsics.implicitCallStmt_InStmt());
        }
        else if (value is VsNewContext @new) {
            return new CSharpNewExpression() {
                Type = GetValue(@new.valueStmt())
            };
        }

        else if (value is VsLiteralContext literal) {
            return new CSharpExpression(GetLiteral(literal.literal()));
        }
        else if (value is VsNotContext not) {
            return new CSharpExpression("!" + GetValue(not.valueStmt()).ToString());
        }

        else if (value is VsStructContext vsstruct) {
            return GetValueList(vsstruct.valueStmt(), ", ");
        }
        else if (value is VsAmpContext amp) {
            return GetValueList(amp.valueStmt(), " + ");
        }
        else if (value is VsEqContext eq) {
            return GetValueList(eq.valueStmt(), " == ");
        }
        else if (value is VsNeqContext neq) {
            return GetValueList(neq.valueStmt(), " != ");
        }
        else if (value is VsGeqContext geq) {
            return GetValueList(geq.valueStmt(), " >= ");
        }
        else if (value is VsGtContext gt) {
            return GetValueList(gt.valueStmt(), " > ");
        }
        else if (value is VsLeqContext leq) {
            return GetValueList(leq.valueStmt(), " <= ");
        }
        else if (value is VsLtContext lt) {
            return GetValueList(lt.valueStmt(), " < ");
        }
        else if (value is VsAddContext add) {
            return GetValueList(add.valueStmt(), " + ");
        }
        else if (value is VsMinusContext min) {
            return GetValueList(min.valueStmt(), " - ");
        }
        else if (value is VsMultContext mul) {
            return GetValueList(mul.valueStmt(), " * ");
        }
        else if (value is VsDivContext div) {
            return GetValueList(div.valueStmt(), " / ");
        }
        else if (value is VsOrContext or) {
            return GetValueList(or.valueStmt(), " || ");
        }
        else if (value is VsAndContext and) {
            return GetValueList(and.valueStmt(), " && ");
        }
        else if (value is VsXorContext xor) {
            return GetValueList(xor.valueStmt(), " ^ ");
        }
        else {
            Trace.TraceWarning("Unknown value: {0} {1}", value.GetType().Name, value.GetText());
            return new CSharpExpression(value.GetText());
        }

        CSharpExpression GetValueList(ValueStmtContext[] values, string separator) 
            => new(string.Join(separator, values.Select(GetValue)));
    }


    CSharpAssignmentStatement GetLet(LetStmtContext let)
    {
        using var _ = new TraceMethod(let);

        var target = let.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext call
            ? GetImplicitCall(call) : throw NotSupported(let);

        var value = let.valueStmt() is ValueStmtContext v
            ? GetValue(v) : throw NotSupported(let);

        return new CSharpAssignmentStatement(target, value);
    }

    CSharpAssignmentStatement GetSet(SetStmtContext set)
    {
        using var _ = new TraceMethod(set);

        var target = set.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext call
            ? GetImplicitCall(call) : throw NotSupported(set);

        var value = set.valueStmt() is ValueStmtContext v
            ? GetValue(v) : throw NotSupported(set);

        return new CSharpAssignmentStatement(target, value);
    }


    CSharpIfStatement GetIf(IfThenElseStmtContext ifthen)
    {
        using var _ = new TraceMethod(ifthen);

        var csif = new CSharpIfStatement();

        if (ifthen is BlockIfThenElseContext @if) {
            if (@if.ifBlockStmt() is IfBlockStmtContext ifBlock) {
                csif.Condition = GetValue(ifBlock.ifConditionStmt().valueStmt());
                csif.Then = ifBlock.block() is BlockContext block ? GetBlock(block) : [];
            }

            CSharpIfStatement cselse = csif;
            
            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var newelse = new CSharpIfStatement {
                        Condition = GetValue(elseif.ifConditionStmt().valueStmt()),
                        Then      = GetBlock(elseif.block())
                    };

                    cselse.Else = [newelse];
                    cselse = newelse;
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                cselse.Else = GetBlock(@else.block());
            }
        }
        else if (ifthen is InlineIfThenElseContext inline && inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
            csif.Condition = GetValue(ifcond.valueStmt());
            csif.Then = GetBlockStatements(inline.blockStmt(0)).ToList();

            if (inline.blockStmt(1) is BlockStmtContext elseBlock) {
                csif.Else = GetBlockStatements(elseBlock).ToList();
            }
        }
        else {
            NotSupported(ifthen);
        }

        return csif;
    }

    CSharpGenericStatement GetForNext(ForNextStmtContext forNext)
    {
        var sb = new StringBuilder();
        sb.Append("for (");

        if (forNext.asTypeClause() is AsTypeClauseContext asType) {
            sb.Append(GetType(asType));
            sb.Append(' ');
        }

        sb.Append(GetVariableOrProcedureCall(forNext.iCS_S_VariableOrProcedureCall()));
        sb.Append(" = ");
        sb.Append(GetValue(forNext.valueStmt(0)));
        sb.Append("; ");

        sb.Append(GetValue(forNext.valueStmt(0)));
        sb.Append(" < ");
        sb.Append(GetValue(forNext.valueStmt(1)));
        sb.Append("; ");

        sb.Append(GetValue(forNext.valueStmt(0)));

        if (forNext.valueStmt(2) is ValueStmtContext step) {
            sb.Append(" += ");
            sb.Append(GetValue(step));
        }
        else {
            sb.Append("++");
        }

        sb.Append(") ");
        sb.Append(new CSharpBlockStatement(GetBlock(forNext.block())).ToString());

        if (forNext.typeHint().Length > 0) {
            NotSupported(forNext.typeHint()[0]);
        }

        return new CSharpGenericStatement(sb.ToString());
    }

    CSharpWhileStatement GetDoLoop(DoLoopStmtContext doloop) => new() {
        Condition = GetValue(doloop.valueStmt()),
        Statements = GetBlock(doloop.block())
    };

    CSharpSelectStatement GetSelect(SelectCaseStmtContext select)
    {
        var csselect = new CSharpSelectStatement {
            Condition = GetValue(select.valueStmt())
        };

        foreach (var caseStmt in select.sC_Case()) {
            var block = GetBlock(caseStmt.block());

            if (caseStmt.sC_Cond() is CaseCondExprContext expr) {
                foreach (var condition in expr.sC_CondExpr()) {
                    if (condition is CaseCondExprValueContext valueCond) {
                        csselect.Cases.Add(new CSharpSelectCaseStatement {
                            Condition = GetValue(valueCond.valueStmt()),
                            Statements = block
                        });
                    }
                    else if (condition is CaseCondExprIsContext isCond) {
                        throw NotSupported(isCond);
                    }
                    else if (condition is CaseCondExprToContext toCond) {
                        throw NotSupported(toCond);
                    }
                    else {
                        throw NotSupported(condition);
                    }
                }
            }
            else if (caseStmt.sC_Cond() is CaseCondElseContext @else) {
                csselect.Default = block;
            }
            else {
                NotSupported(caseStmt);
            }
        }

        return csselect;
    }


    CSharpBlockStatement GetWith(WithStmtContext with)
    {
        using var _ = new TraceMethod(with);

        PushContext(callee: with.implicitCallStmt_InStmt());
        try {
            return new CSharpBlockStatement(GetBlock(with.block()));
        }
        finally {
            _contexts.Pop();
        }
    }

    CSharpGenericStatement GetResume(ResumeStmtContext resume)
    {
        if (resume.INTEGERLITERAL() is ITerminalNode literal) {
            return new CSharpGenericStatement($"// goto {literal.GetText()};");
        }
        else if (resume.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
            return new CSharpGenericStatement($"goto {GetIdentifier(identifier)};");
        }
        else if (resume.NEXT() is ITerminalNode terminal) {
            return new CSharpGenericStatement("// Resume Next;");
        }
        else {
            throw NotSupported(resume);
        }
    }


    CSharpGenericStatement GetOpen(OpenStmtContext open)
    {
        var sb = new StringBuilder();

        sb.Append("var ");
        sb.Append(GetValue(open.valueStmt(1)));
        sb.Append(" = new StreamWriter(File.Open(");

        sb.Append(GetValue(open.valueStmt(0)));

        if (open.READ_WRITE() != null) {
            sb.Append(", FileAccess.ReadWrite, ");
        }
        else if (open.READ() != null) {
            sb.Append(", FileAccess.Read, ");
        }
        else if (open.WRITE() != null) {
            sb.Append(", FileAccess.Write, ");
        }

        if (open.LOCK_READ_WRITE() != null) {
            sb.Append(", FileShare.None");
        }
        else if (open.LOCK_WRITE() != null) {
            sb.Append(", FileShare.Read");
        }
        else if (open.LOCK_READ() != null) {
            sb.Append(", FileShare.Write");
        }
        else if (open.SHARED() != null) {
            sb.Append(", FileShare.ReadWrite");
        }

        sb.Append("));");
        return new CSharpGenericStatement(sb.ToString());
    }

    IEnumerable<CSharpGenericStatement> GetErase(EraseStmtContext erase)
    {
        foreach (var value in erase.valueStmt()) {
            var sb = new StringBuilder();
            sb.Append("File.Delete(");
            sb.Append(GetValue(value));
            sb.Append(");");
            yield return new CSharpGenericStatement(sb.ToString());
        }
    }

    CSharpGenericStatement GetPrint(PrintStmtContext print)
    {
        var sb = new StringBuilder();
        sb.Append(GetValue(print.valueStmt()));
        sb.Append(".Write(");

        if (print.outputList() is OutputListContext outputs) {
            foreach (var output in outputs.outputList_Expression()) {
                sb.Append(", ");

                if (output.valueStmt() is ValueStmtContext value) {
                    sb.Append(GetValue(value));
                }
                if (output.argsCall() is ArgsCallContext argsCall) {
                    sb.Append(GetArgsCall(argsCall, false));
                }

            }
        }

        sb.Append(')');
        return new CSharpGenericStatement(sb.ToString());
    }

    CSharpGenericStatement GetClose(CloseStmtContext close)
    {
        var sb = new StringBuilder();
        sb.Append(GetValue(close.valueStmt(0)));
        sb.Append(".Dispose()");
        return new CSharpGenericStatement(sb.ToString());
    }


    CSharpCallStatement GetImplicitCall(ImplicitCallStmt_InBlockContext call)
    {
        using var _ = new TraceMethod(call);

        if (call.iCS_B_ProcedureCall() is ICS_B_ProcedureCallContext proc) {
            return new CSharpCallStatement(GetProcedureCall(proc));
        }
        else if (call.iCS_B_MemberProcedureCall() is ICS_B_MemberProcedureCallContext member) {
            return new CSharpCallStatement(GetMemberProcedureCall(member));
        }
        else {
            throw NotSupported(call);
        }

        CSharpCallValue GetProcedureCall(ICS_B_ProcedureCallContext proc)
        {
            using var _ = new TraceMethod(proc);

            var call = new CSharpCallValue {
                Type = CSharpCallValue.CallType.Method,
                Arguments = GetArgsCall(proc.argsCall()).ToList()
            };

            if (proc.certainIdentifier() is CertainIdentifierContext certain) {
                call.Callee = new CSharpExpression(GetIdentifier(certain));
            }
            else {
                throw NotSupported(proc);
            }

            return call;
        }

        CSharpCallValue GetMemberProcedureCall(ICS_B_MemberProcedureCallContext member)
        {
            using var _ = new TraceMethod(member);

            var call = new CSharpCallValue();
            call.Type = CSharpCallValue.CallType.Method;

            if (member.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext callInStmt) {
                call.Callee = GetImplicitCall(callInStmt);
            }
            else if (_contexts.TryPeek(out var context) && context.ImplicitCallee != null) {
                call.Callee = GetImplicitCall(context.ImplicitCallee);
            }
            else {
                throw new InvalidOperationException("Member procedure call without callee.");
            }

            if (member.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
                call.Members.Add(new CSharpExpression(GetIdentifier(identifier)));
            }
            else {
                throw new InvalidOperationException("Member procedure call without identifier.");
            }

            if (member.argsCall() is ArgsCallContext args) {
                call.Arguments = GetArgsCall(args).ToList();
            }

            if (member.dictionaryCallStmt() is DictionaryCallStmtContext dic) {
                NotSupported(dic);
            }

            return call;
        }
    }

    CSharpCallValue GetImplicitCall(ImplicitCallStmt_InStmtContext call)
    {
        using var _ = new TraceMethod(call);

        if (call.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext vpcall) {
            return GetVariableOrProcedureCall(vpcall);
        }
        else if (call.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext pacall) {
            return GetProcedureArray(pacall);
        }
        else if (call.iCS_S_MembersCall() is ICS_S_MembersCallContext membersCall) {
            return GetImplicitMembersCall(membersCall);
        }
        else if (call.iCS_S_DictionaryCall() is ICS_S_DictionaryCallContext dictionaryCall) {
            return GetImplicitDictionaryCall(dictionaryCall);
        }
        else {
            throw NotSupported(call);
        }

        CSharpCallValue GetImplicitMembersCall(ICS_S_MembersCallContext membersCall)
        {
            using var _ = new TraceMethod(membersCall);

            var call = new CSharpCallValue();

            if (membersCall.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varOrProc) {
                call.Callee = GetVariableOrProcedureCall(varOrProc);
            }
            else if (membersCall.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext proc) {
                call.Callee = GetProcedureArray(proc);
            }
            else if (_contexts.TryPeek(out var context) && context.ImplicitCallee != null) {
                call.Callee = GetImplicitCall(context.ImplicitCallee);
            }

            if (membersCall.iCS_S_MemberCall() is ICS_S_MemberCallContext[] members && members.Length > 0) {
                foreach (var member in members) {
                    call.Members.Add(GetMemberCall(member));
                }
            }

            if (membersCall.dictionaryCallStmt() is DictionaryCallStmtContext dict) {
                NotSupported(dict);
            }

            return call;

            CSharpCallValue GetMemberCall(ICS_S_MemberCallContext memberCall)
            {
                using var _ = new TraceMethod(memberCall);

                if (memberCall.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varOrProc) {
                    return GetVariableOrProcedureCall(varOrProc);
                }
                else if (memberCall.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext proc) {
                    return GetProcedureArray(proc);
                }
                else {
                    throw NotSupported(memberCall);
                }
            }
        }

        CSharpCallValue GetImplicitDictionaryCall(ICS_S_DictionaryCallContext dictionaryCall)
        {
            var cscall = new CSharpCallValue();

            if (_contexts.Peek().ImplicitCallee is ImplicitCallStmt_InStmtContext stmt) {
                cscall.Callee = GetImplicitCall(stmt);
            }

            var dc = dictionaryCall.dictionaryCallStmt();
            cscall.Arguments = [ new CSharpArgumentCall(new CSharpExpression(GetIdentifier(dc.ambiguousIdentifier()))) ];
            cscall.Type = CSharpCallValue.CallType.Dictionary;
            return cscall;
        }
    }

    CSharpCallValue GetVariableOrProcedureCall(ICS_S_VariableOrProcedureCallContext varOrProc)
    {
        using var _ = new TraceMethod(varOrProc);

        if (varOrProc.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
            var call = new CSharpCallValue {
                Callee = new CSharpExpression(GetIdentifier(identifier))
            };

            if (varOrProc.dictionaryCallStmt() is DictionaryCallStmtContext dc) {
                call.Arguments = [new CSharpArgumentCall(new CSharpExpression(GetIdentifier(dc.ambiguousIdentifier())))];
                call.Type = CSharpCallValue.CallType.Dictionary;
            }

            return call;
        }
        else {
            throw NotSupported(varOrProc);
        }
    }

    CSharpCallValue GetProcedureArray(ICS_S_ProcedureOrArrayCallContext call)
    {
        using var _ = new TraceMethod(call);

        if (call.iCS_S_NestedProcedureCall() is ICS_S_NestedProcedureCallContext nested) {
            throw NotSupported(nested);
        }
        if (call.baseType() is BaseTypeContext type) {
            throw NotSupported(type);
        }

        var identifier = GetIdentifier(call.ambiguousIdentifier(), call.typeHint());
        var variable   = _contexts.Peek().Variables.TryGetValue(identifier, out var info) == true ? info : null;

        var cscall = new CSharpCallValue {
            Callee = new CSharpExpression(identifier),
            Type = variable?.ArrayDimensions > 0 ? CSharpCallValue.CallType.Array : CSharpCallValue.CallType.Method
        };

        foreach (var args in call.argsCall()) {
            cscall.Arguments.AddRange(GetArgsCall(args));
        }

        return cscall;
    }

    

    IEnumerable<CSharpArgumentCall> GetArgsCall(ArgsCallContext args)
    {
        if (args is null) yield break;

        foreach (var arg in args.argCall()) {
            if (arg.valueStmt() is VsAssignContext assign) {
                yield return new CSharpArgumentCall(
                    value: GetValue(assign.valueStmt()),
                    name: GetImplicitCall(assign.implicitCallStmt_InStmt()));

            }
            else {
                yield return new CSharpArgumentCall(GetValue(arg.valueStmt()));
            }
        }
    }

    string GetArgsCall(ArgsCallContext args, bool dictionary)
    {
        string GetArg(ArgCallContext arg) => GetValue(arg.valueStmt()).ToString();

        string[] arguments = args.argCall().Select(GetArg).ToArray();
        if (dictionary) {
            return $"[{string.Join(", ", arguments)}]";
        }
        else {
            return $"({string.Join(", ", arguments)})";
        }
    }

    
    static string GetLiteral(LiteralContext lit)
    {
        if (lit.COLORLITERAL() is ITerminalNode color) {
            var hex = "0x" + color.Symbol.Text[2..].TrimEnd(['&']).PadLeft(6, '0').PadLeft(8, 'F');

            var col = System.Drawing.Color.FromArgb(Convert.ToInt32(hex, 16));

            if (col.IsNamedColor) {
                return $"Color.FromName(\"{col.Name}\")";
            }
            else if (col.A == 255) {
                return $"Color.FromArgb({col.R}, {col.G}, {col.B})";
            }
            else {
                return $"Color.FromArgb({col.A}, {col.R}, {col.G}, {col.B})";
            }
        }
        else if (lit.FILENUMBER() is ITerminalNode file) {
            return file.GetText().TrimStart('#');
        }
        else if (lit.TRUE() is not null) {
            return "true";
        }
        else if (lit.FALSE() is not null) {
            return "false";
        }
        else if (lit.NOTHING() is not null || lit.NULL() is not null) {
            return "null";
        }
        else if (lit.INTEGERLITERAL() is ITerminalNode @short) {
            return @short.GetText();
        }
        else if (lit.DOUBLELITERAL() is ITerminalNode @double) {
            return @double.GetText().TrimEnd(['&']);
        }
        else {
            return lit.GetText();
        }
    }

    static CSharpType GetType(AsTypeClauseContext asType)
    {
        if (asType == null) {
            return CSharpType.Unknown;
        }

        if (asType.fieldLength() is FieldLengthContext length) {
            throw NotSupported(length);
        }

        return GetType(asType.type());
    }

    static CSharpType GetType(TypeContext type)
    {
        var isArray = type.LPAREN() != null || type.RPAREN() != null;

        if (type.complexType() is ComplexTypeContext complex) {
            return new CSharpType(complex.GetText(), isArray);
        }
        else {
            var baseType = type.baseType();
            var typeSymbol = ((ITerminalNode)baseType.GetChild(0)).Symbol;
            var typeName = typeSymbol.Type switch {
                BOOLEAN    => "bool",
                BYTE       => "byte",
                COLLECTION => "System.Collections.ICollection",
                DATE       => "DateTime",
                DOUBLE     => "double",
                INTEGER    => "short",
                LONG       => "int",
                SINGLE     => "float",
                STRING     => "string",
                OBJECT     => "object",
                VARIANT    => "object",
                _ => typeSymbol.Text
            };

            return new CSharpType(typeName, isArray);
        }
    }

    static CSharpVisibility GetVisibility(PublicPrivateVisibilityContext v)
    {
        if (v.PRIVATE() != null) {
            return CSharpVisibility.Private;
        }
        else if (v.PUBLIC() != null) {
            return CSharpVisibility.Public;
        }
        else {
            throw NotSupported(v);
        }
    }

    static CSharpVisibility GetVisibility(VisibilityContext v)
    {
        if (v is null) {
            return CSharpVisibility.Private;
        }
        else if (v.PRIVATE() != null) {
            return CSharpVisibility.Private;
        }
        else if (v.PUBLIC() != null || v.GLOBAL() != null) {
            return CSharpVisibility.Public;
        }
        else if (v.FRIEND() != null) {
            return CSharpVisibility.Internal;
        }
        else {
            throw NotSupported(v);
        }
    }

    static string GetIdentifier(AmbiguousIdentifierContext identifier, TypeHintContext typeHint = null)
    {
        if (identifier is null) {
            throw new ArgumentNullException(nameof(identifier));
        }

        using var _ = new TraceMethod(identifier);

        string cast = "";
        if (typeHint is not null) {
            if (typeHint.DOLLAR() is not null) {
                cast = "(string)";
            }
            else {
                throw NotSupported(typeHint);
            }
        }

        string text = identifier.GetText();

        if (text == "vbNullString") {
            text = "string.Empty";
        }
        
        return cast + text;
    }

    static string GetIdentifier(CertainIdentifierContext certain)
    {
        return certain.GetText();
    }


    [DebuggerHidden]
    static Exception NotSupported(ParserRuleContext context, [CallerMemberName] string caller = null)
    {
        throw new NotSupportedException($"Not supported from {caller}: '{context.GetText()}'\r\n{new ConsoleVisitor().Visit(context)}");
    }

    class TraceMethod : IDisposable
    {
        public TraceMethod(ParserRuleContext ctx, [CallerMemberName] string procedure = null)
        {
            Trace.TraceInformation($"{procedure} {ctx.GetType().Name} {ctx.Start}");
            Trace.Indent();
        }

        public void Dispose()
        {
            Trace.Unindent();
        }
    }
}
