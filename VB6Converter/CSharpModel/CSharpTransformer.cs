using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VB6Parser;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.CSharpModel;

partial class CSharpTransformer()
{
    readonly Stack<CSharpClass> _class = [];
    readonly Stack<CSharpMethod> _method = [];
    readonly Stack<CSharpBlockStatement> _block = [];
    readonly Stack<CSharpCallExpression> _with = [];

    CSharpVariable FindVariable(string name)
    {
        foreach (var block in _block) {
            foreach (var dec in block.GetDeclarations()) {
                if (dec.Variable.Name == name) {
                    return dec.Variable;
                }
            }
        }

        foreach (var m in _method) {
            foreach (var dec in m.Arguments) {
                if (dec.Variable.Name == name) {
                    return dec.Variable;
                }
            }
        }

        foreach (var c in _class) {
            foreach (var dec in c.Members.OfType<CSharpDeclarationStatement>()) {
                if (dec.Variable.Name == name) {
                    return dec.Variable;
                }
            }
        }

        return null;
    }

       

    public CSharpClass GetClass(ModuleContext module, string name)
    {
        using var _ = new TraceMethod(module);

        var c = new CSharpClass(CSharpVisibility.Public, name) { 
            IsPartial = true, 
            IsStatic = true 
        };

        var body = module.moduleBody();
        var bodyElements = body.moduleBodyElement();

        // Collect sub-objects
        foreach (var e in bodyElements) {
            if (e.enumerationStmt() is EnumerationStmtContext enumCtx) {
                c.Members.Add(GetEnum(enumCtx));
            }
            else if (e.typeStmt() is TypeStmtContext typeCtx) {
                c.Members.Add(GetStruct(typeCtx));
            }
        }
        
        // Collect all declarations first
        foreach (var block in bodyElements.Select(e => e.moduleBlock()).OfType<ModuleBlockContext>()) {
            var statements = GetBlock(block.block());
            foreach (var statement in statements) {
                if (statement is CSharpDeclarationStatement decl) {
                    c.Members.Add(decl);
                }
                else {
                    throw new NotSupportedException("Class statement not supported.");
                }
            }
        }

        foreach (var e in bodyElements) {
            if (e.moduleBlock() is not null) {
                continue; // Already processed
            }
            else if (e.enumerationStmt() is EnumerationStmtContext) {
                continue; // Handled by namespace
            }
            else if (e.typeStmt() is TypeStmtContext) {
                continue; // Handled by namespace
            }

            else if (e.subStmt() is SubStmtContext sub) {
                c.Members.Add(GetMethod(sub));
            }
            else if (e.functionStmt() is FunctionStmtContext func) {
                c.Members.Add(GetMethod(func));
            }
            else if (e.declareStmt() is DeclareStmtContext declare) {
                c.Members.Add(GetExtern(declare));
            }

            else if (e.propertyGetStmt() is PropertyGetStmtContext get) {
                var propName = GetIdentifier(get.ambiguousIdentifier());
                var property = c.GetOrCreateProperty(propName);

                property.IsStatic = get.STATIC() is not null;
                property.Type = GetType(get.asTypeClause());
                property.Getter = (GetVisibility(get.visibility()), new CSharpBlockStatement(GetBlock(get.block())));
            }
            else if (e.propertySetStmt() is PropertySetStmtContext set) {
                var propName = GetIdentifier(set.ambiguousIdentifier());
                var property = c.GetOrCreateProperty(propName);

                var arguments = GetMethodArguments(set.argList());
                if (arguments.Count > 0) {
                    property.Type = arguments[0].Variable.Type;
                }

                property.IsStatic = set.STATIC() is not null;
                property.Setter = (GetVisibility(set.visibility()), new CSharpBlockStatement(GetBlock(set.block())), arguments[0].Variable);
            }
            else if (e.propertyLetStmt() is PropertyLetStmtContext let) {
                var propName = GetIdentifier(let.ambiguousIdentifier());
                var property = c.GetOrCreateProperty(propName);

                var arguments = GetMethodArguments(let.argList());
                if (arguments.Count > 0) {
                    property.Type = arguments[0].Variable.Type;
                }

                property.IsStatic = let.STATIC() is not null;
                property.Setter = (GetVisibility(let.visibility()), new CSharpBlockStatement(GetBlock(let.block())), arguments[0].Variable);
            }

            else {
                NotSupported(e);
            }
        }

        return c;
    }

    CSharpStruct GetStruct(TypeStmtContext type)
    {
        using var _ = new TraceMethod(type);
        return new(
            GetVisibility(type.visibility()),
            GetIdentifier(type.ambiguousIdentifier()),
            type.typeStmt_Element().Select(e => {
                return new CSharpVariable(
                    Name: GetIdentifier(e.ambiguousIdentifier()),
                    Type: GetType(e.asTypeClause())
                );
            }).ToArray());
    }

    CSharpEnum GetEnum(EnumerationStmtContext e)
    {
        using var _ = new TraceMethod(e);

        return new CSharpEnum(
            GetVisibility(e.publicPrivateVisibility()),
            GetIdentifier(e.ambiguousIdentifier())) {
            Values = e.enumerationStmt_Constant().Select(c => {
                var name = GetIdentifier(c.ambiguousIdentifier());
                var value = c.valueStmt() is ValueStmtContext v ? GetValue(v) : null;
                return (name, value);
            }).ToList()
        };
    }


    // Methods

    CSharpMethod GetMethod(SubStmtContext sub)
    {
        using var _ = new TraceMethod(sub);

        var name = GetIdentifier(sub.ambiguousIdentifier());
        var visb = GetVisibility(sub.visibility());
        var args = GetMethodArguments(sub.argList());

        var method = new CSharpMethod(name) {
            ReturnType = CSharpType.Void,
            Visibility = visb,
            Arguments  = args
        };

        _method.Push(method);
        try {
            method.Statements = GetBlock(sub.block());
            return method;
        }
        finally {
            _method.Pop();
        }
    }

    CSharpMethod GetMethod(FunctionStmtContext func)
    {
        using var _ = new TraceMethod(func);

        var name = GetIdentifier(func.ambiguousIdentifier());
        var visb = GetVisibility(func.visibility());
        var args = GetMethodArguments(func.argList());

        var method = new CSharpMethod(name) {
            ReturnType = GetType(func.asTypeClause()),
            Visibility = visb,
            Arguments  = args
        };

        _method.Push(method);
        try {
            method.Statements = GetBlock(func.block());
            return method;
        }
        finally {
            _method.Pop();
        }
    }

    CSharpExtern GetExtern(DeclareStmtContext declare)
    {
        using var _ = new TraceMethod(declare);

        return new CSharpExtern() {
            Visibility = GetVisibility(declare.visibility()),
            Library = declare.STRINGLITERAL(0).GetText(),
            Type = GetType(declare.asTypeClause()),
            Name = GetIdentifier(declare.ambiguousIdentifier()),
            Alias = declare.STRINGLITERAL(1)?.GetText()?.Trim('"'),
            Arguments = [.. GetMethodArguments(declare.argList())]
        };
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

    CSharpBlockStatement GetBlock(BlockContext blockCtx)
    {
        using var _ = new TraceMethod(blockCtx);

        if (blockCtx is null) {
            return CSharpBlockStatement.Empty;
        }

        var block = new CSharpBlockStatement();

        _block.Push(block);
        try {
            foreach (var stmt in blockCtx.blockStmt()) {
                block.Statements.AddRange(GetBlockStatements(stmt));
            }

            return block;
        }
        finally {
            _block.Pop();
        }
    }

    IEnumerable<ICSharpStatement> GetBlockStatements(BlockStmtContext stmt)
    {
        using var _ = new TraceMethod(stmt);

        if (stmt.lineLabel() is LineLabelContext label) {
            string name = label.ambiguousIdentifier() is AmbiguousIdentifierContext id ? GetIdentifier(id) : label.GetText();
            return [new CSharpLabelStatement { Name = name }];
        }


        else if (stmt.constStmt() is ConstStmtContext @const) {
            return GetConstants(@const);
        }
        else if (stmt.variableStmt() is VariableStmtContext var) {
            return GetVariables(var);
        }

        else if (stmt.redimStmt() is RedimStmtContext redim) {
            return GetRedim(redim);
        }

        else if (stmt.implicitCallStmt_InBlock() is ImplicitCallStmt_InBlockContext implicitStmt) {
            return [GetCallStatement(implicitStmt)];
        }
        else if (stmt.explicitCallStmt() is ExplicitCallStmtContext explicitCallStmt) {
            return [GetCallStatement(explicitCallStmt)];
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
        else if (stmt.forEachStmt() is ForEachStmtContext forEach) {
            return [GetForEach(forEach)];
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

        else if (stmt.loadStmt() is LoadStmtContext load) {
            return [new CSharpGenericStatement($"// {load.GetText()}")];
        }
        else if (stmt.unloadStmt() is UnloadStmtContext unload) {
            return [new CSharpGenericStatement($"// {unload.GetText()}")];
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
        }

        else if (stmt.endStmt() is EndStmtContext endStmt) {
            return [new CSharpGenericStatement("Application.Exit();")];
        }

        else if (stmt.sendkeysStmt() is SendkeysStmtContext sendKeys) {
            return sendKeys.valueStmt().Select(v => new CSharpGenericStatement($"SendKeys.Send(\"{v}\");"));
        }

        else {
            throw NotSupported(stmt);
        }
    }

    IEnumerable<CSharpRedimStatement> GetRedim(RedimStmtContext redim)
    {
        using var _ = new TraceMethod(redim);

        foreach (var stmt in redim.redimSubStmt()) {
            yield return new CSharpRedimStatement() {
                Variable   = GetExpressionCall(stmt.implicitCallStmt_InStmt()),
                Type       = GetType(stmt.asTypeClause()),
                Subscripts = stmt.subscripts().subscript().Select(s => GetValue(s.valueStmt(0))).ToList()
            };
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

            CSharpGenericExpression value = null;
            if (arrayDimensions.Count > 0) {
                value = new CSharpGenericExpression("new " + variable.Type + "[" + string.Join(", ", arrayDimensions) + "]");
            }

            yield return new CSharpDeclarationStatement(variable, value, option, visibility);
        }
    }


    CSharpAssignmentStatement GetLet(LetStmtContext let)
    {
        using var _ = new TraceMethod(let);

        var target = let.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext call
            ? GetExpressionCall(call) : throw NotSupported(let);

        var value = let.valueStmt() is ValueStmtContext v
            ? GetValue(v) : throw NotSupported(let);

        return new CSharpAssignmentStatement(target, value);
    }

    CSharpAssignmentStatement GetSet(SetStmtContext set)
    {
        using var _ = new TraceMethod(set);

        var target = set.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext call
            ? GetExpressionCall(call) : throw NotSupported(set);

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
                csif.Then = GetBlock(ifBlock.block());
            }

            CSharpIfStatement cselse = csif;
            
            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var newelse = new CSharpIfStatement {
                        Condition = GetValue(elseif.ifConditionStmt().valueStmt()),
                        Then      = GetBlock(elseif.block())
                    };

                    cselse.Else = new CSharpBlockStatement([newelse]);
                    cselse = newelse;
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                cselse.Else = GetBlock(@else.block());
            }
        }
        else if (ifthen is InlineIfThenElseContext inline && inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
            csif.Condition = GetValue(ifcond.valueStmt());
            csif.Then = new CSharpBlockStatement(GetBlockStatements(inline.blockStmt(0)));

            if (inline.blockStmt(1) is BlockStmtContext elseBlock) {
                csif.Else = new CSharpBlockStatement(GetBlockStatements(elseBlock));
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

        sb.Append(GetVariableOrProcedureCall(forNext.iCS_S_VariableOrProcedureCall(), CallType.Unspecified));
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
    
    CSharpForEachStatement GetForEach(ForEachStmtContext forEach)
    {
        using var _ = new TraceMethod(forEach);

        return new CSharpForEachStatement() {
            Variable = GetIdentifierReference(forEach.ambiguousIdentifier(0), forEach.typeHint()),
            Enumerator = GetValue(forEach.valueStmt()),
            Statements = new CSharpBlockStatement(GetBlock(forEach.block()))
        };
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

        _with.Push(GetExpressionCall(with.implicitCallStmt_InStmt()));
        try {
            return new CSharpBlockStatement(GetBlock(with.block()));
        }
        finally {
            _with.Pop();
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

    CSharpPrintStatement GetPrint(PrintStmtContext print) => new() {
        Target = GetValue(print.valueStmt()),

        Outputs = print.outputList().outputList_Expression().SelectMany(o => {
            if (o.valueStmt() is ValueStmtContext value) {
                return [GetValue(value)];
            }
            else if (o.argsCall() is ArgsCallContext argsCall) {
                return GetArgsCall(argsCall).Cast<ICSharpExpression>();
            }
            else {
                throw NotSupported(o);
            }
        }).ToList()
    };

    CSharpGenericStatement GetClose(CloseStmtContext close)
    {
        var sb = new StringBuilder();
        sb.Append(GetValue(close.valueStmt(0)));
        sb.Append(".Dispose()");
        return new CSharpGenericStatement(sb.ToString());
    }


    CSharpCallStatement GetCallStatement(ICallStatementContext call)
    {
        using var _ = new TraceMethod(call);

        if (call.procedureCall() is ICallContext proc) {
            return new CSharpCallStatement(GetVariableOrProcedureCall(proc, CallType.Invoke));
        }
        else if (call.memberProcedureCall() is IMemberProcedureCallContext member) {
            return new CSharpCallStatement(GetMemberProcedureCall(member));
        }
        else {
            throw NotSupported(call);
        }

        CSharpCallExpression GetMemberProcedureCall(IMemberProcedureCallContext memberCtx)
        {
            using var _ = new TraceMethod(memberCtx);

            CSharpCallExpression callee;

            if (memberCtx.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext callInStmt) {
                if (callInStmt.iCS_S_MembersCall() is ICS_S_MembersCallContext members) {
                    callee = GetMembersCallExpression(callInStmt.iCS_S_MembersCall(), CallType.Variable);
                }
                else if (callInStmt.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varProc)
                {
                    callee = GetVariableOrProcedureCall(varProc, CallType.Variable);
                }
                else {
                    throw NotSupported(callInStmt);
                }
            }
            else if (_with.TryPeek(out var with)) {
                callee = with;
            }
            else {
                throw new InvalidOperationException("Member procedure call without callee.");
            }

            var target = memberCtx.identifier() is IIdentifierContext identifier
                ? GetIdentifierReference(identifier, memberCtx.typeHint())
                : throw new InvalidOperationException("Member procedure call without target.");

            return GetCall(memberCtx, target, callee, CallType.Invoke);
        }
    }


    CSharpCallExpression GetExpressionCall(ImplicitCallStmt_InStmtContext call)
    {
        using var _ = new TraceMethod(call);

        if (call.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext vpcall) {
            return GetVariableOrProcedureCall(vpcall);
        }
        else if (call.iCS_S_MembersCall() is ICS_S_MembersCallContext membersCall) {
            return GetMembersCallExpression(membersCall, CallType.Invoke);
        }
        else if (call.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext pacall) {
            return GetProcedureArray(pacall);
        }
        else {
            throw NotSupported(call);
        }
    }


    // calling a variable or procedure directly
    CSharpCallExpression GetVariableOrProcedureCall(ICallContext proc, CallType type = CallType.Unspecified)
    {
        using var _ = new TraceMethod(proc);        
        var target = GetIdentifierReference(proc.identifier(), proc.typeHint());
        return GetCall(proc, target, null, type);
    }   

    CSharpCallExpression GetMembersCallExpression(ICS_S_MembersCallContext membersCall, CallType type = CallType.Unspecified)
    {
        using var _ = new TraceMethod(membersCall);

        CSharpCallExpression call = null;

        var chain = new[] { (IMemberCallContext)membersCall }.Concat(membersCall.iCS_S_MemberCall());
        foreach (var member in chain) {
            var memberCall = GetMemberCallExpression(member, CallType.Variable);
            memberCall.Callee = call;
            call = memberCall;
        }

        call.Type = type;
        return call;
        
        CSharpCallExpression GetMemberCallExpression(IMemberCallContext memberCall, CallType type)
        {
            using var _ = new TraceMethod(memberCall);

            if (memberCall.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varOrProc) {
                return GetVariableOrProcedureCall(varOrProc, type);
            }
            else if (memberCall.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext procOrArray) {
                return GetProcedureArray(procOrArray);
            }
            else {
                throw NotSupported(memberCall);
            }
        }
    }

    CSharpCallExpression GetProcedureArray(ICS_S_ProcedureOrArrayCallContext call, CallType type = CallType.Unspecified)
    {
        using var _ = new TraceMethod(call);

        if (call.iCS_S_NestedProcedureCall() is ICS_S_NestedProcedureCallContext nested) {
            throw NotSupported(nested);
        }
        if (call.baseType() is BaseTypeContext baseType) {
            throw NotSupported(baseType);
        }

        return GetVariableOrProcedureCall(call, type);
    }

    CSharpCallExpression GetCall(ICallContext proc, CSharpIdentifierExpression target, CSharpCallExpression callee, CallType type)
    {
        using var _ = new TraceMethod(proc);

        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        var call = new CSharpCallExpression(target) { Type = type, Callee = callee };

        foreach (var args in proc.args()) {
            call.Arguments.AddRange(GetArgsCall(args));
        }

        if (proc.dictionaryCallStmt() is DictionaryCallStmtContext dc) {
            var identifier = GetIdentifierReference(dc.ambiguousIdentifier(), dc.typeHint());

            call.Type = CallType.Dictionary;
            call.Arguments = [
                new CSharpArgumentCall(identifier)
            ];
        }

        return call;
    }


    ICSharpExpression GetValue(ValueStmtContext value)
    {
        using var _ = new TraceMethod(value);

        if (value is VsICSContext vsics) {
            return GetExpressionCall(vsics.implicitCallStmt_InStmt());
        }
        else if (value is VsNewContext @new) {
            return new CSharpNewExpression() {
                Type = GetValue(@new.valueStmt())
            };
        }

        else if (value is VsLiteralContext literal) {
            return new CSharpGenericExpression(GetLiteral(literal.literal()));
        }
        else if (value is VsNotContext not) {
            return new CSharpGenericExpression("!" + GetValue(not.valueStmt()).ToString());
        }

        else if (value is VsStructContext vsstruct) {
            return GetOperator(vsstruct.valueStmt(), ", ");
        }

        else if (value is VsAmpContext amp) {
            return GetOperator(amp.valueStmt(), " + ");
        }
        else if (value is VsEqContext eq) {
            return GetOperator(eq.valueStmt(), " == ");
        }
        else if (value is VsNeqContext neq) {
            return GetOperator(neq.valueStmt(), " != ");
        }
        else if (value is VsGeqContext geq) {
            return GetOperator(geq.valueStmt(), " >= ");
        }
        else if (value is VsGtContext gt) {
            return GetOperator(gt.valueStmt(), " > ");
        }
        else if (value is VsLeqContext leq) {
            return GetOperator(leq.valueStmt(), " <= ");
        }
        else if (value is VsLtContext lt) {
            return GetOperator(lt.valueStmt(), " < ");
        }
        else if (value is VsAddContext add) {
            return GetOperator(add.valueStmt(), " + ");
        }
        else if (value is VsMinusContext min) {
            return GetOperator(min.valueStmt(), " - ");
        }
        else if (value is VsMultContext mul) {
            return GetOperator(mul.valueStmt(), " * ");
        }
        else if (value is VsDivContext div) {
            return GetOperator(div.valueStmt(), " / ");
        }
        else if (value is VsOrContext or) {
            return GetOperator(or.valueStmt(), " || ");
        }
        else if (value is VsAndContext and) {
            return GetOperator(and.valueStmt(), " && ");
        }
        else if (value is VsXorContext xor) {
            return GetOperator(xor.valueStmt(), " ^ ");
        }
        else if (value is VsModContext mod) {
            return GetOperator(mod.valueStmt(), " % ");
        }
        else if (value is VsIsContext vsis) {
            return GetOperator(vsis.valueStmt(), " is ");
        }

        else {
            Trace.TraceWarning("Unknown value: {0} {1}", value.GetType().Name, value.GetText());
            return new CSharpGenericExpression(value.GetText());
        }

        ICSharpExpression GetOperator(ValueStmtContext[] values, string separator)
            => new CSharpOperatorExpression(separator, values.Select(GetValue).ToArray());
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
        else if (lit.STRINGLITERAL() is ITerminalNode @string) {
            string str = @string.GetText();

            if (str.Contains('\\')) {
                str = str.Replace("\\", "\\\\");
            }

            return str;
        }
        else {
            return lit.GetText();
        }
    }


    IEnumerable<CSharpArgumentCall> GetArgsCall(ArgsCallContext args)
    {
        if (args is null) yield break;

        foreach (var arg in args.argCall()) {
            if (arg.valueStmt() is VsAssignContext assign) {
                yield return new CSharpArgumentCall(
                    value: GetValue(assign.valueStmt()),
                    name: GetExpressionCall(assign.implicitCallStmt_InStmt())
                );

            }
            else {
                yield return new CSharpArgumentCall(GetValue(arg.valueStmt()));
            }
        }
    }


    static CSharpIdentifierExpression GetIdentifierReference(IIdentifierContext identifier, TypeHintContext typeHint)
    {
        if (identifier is null) {
            throw new ArgumentNullException(nameof(identifier));
        }

        using var _ = new TraceMethod(identifier);

        return new CSharpIdentifierExpression(GetIdentifier(identifier), GetType(typeHint));
    }

    static string GetIdentifier(IIdentifierContext identifier)
    {
        if (identifier is null) {
            throw new ArgumentNullException(nameof(identifier));
        }

        using var _ = new TraceMethod(identifier);

        var text = identifier.GetText();
        if (string.Equals(text, "Me", StringComparison.InvariantCultureIgnoreCase)) {
            text = "this";
        }
        else if (string.Equals(text, "vbNullString", StringComparison.InvariantCultureIgnoreCase)) {
            text = "string.Empty";
        }

        return text;
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

    static CSharpType GetType(TypeHintContext typeHint)
    {
        if (typeHint is not null) {
            if (typeHint.DOLLAR() is not null) {
                return CSharpType.String;
            }
            else {
                throw NotSupported(typeHint);
            }
        }
        else {
            return CSharpType.Unknown;
        }
    }

    static CSharpVisibility GetVisibility(PublicPrivateVisibilityContext v)
    {
        if (v is null) {
            return CSharpVisibility.Public;
        }
        else if (v.PRIVATE() != null) {
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


    [DebuggerHidden]
    static Exception NotSupported(object context, [CallerMemberName] string caller = null)
    {
        if (context is ParserRuleContext parser) {
            throw new NotSupportedException($"Not supported from {caller}: '{parser.GetText()}'\r\n{new ConsoleVisitor().Visit(parser)}");
        }
        else {
            throw new NotSupportedException($"Not supported from {caller}: '{context.GetType().Name}'");
        }
    }

    class TraceMethod : IDisposable
    {
        public TraceMethod(object ctx, [CallerMemberName] string procedure = null)
        {
#if DEBUG
            Trace.TraceInformation($"{procedure} {ctx?.GetType().Name} {(ctx as ParserRuleContext)?.Start}");
            Trace.Indent();
#endif
        }

        public void Dispose()
        {
#if DEBUG
            Trace.Unindent();
#endif
        }
    }
}
