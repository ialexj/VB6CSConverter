using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using static VisualBasic6Parser;

namespace VB6Converter;

class CSharpConverter(StatementBuilder sb, IEnumerable<VariableInfo> variables = null)
{
    Stack<ImplicitCallStmt_InStmtContext> _with = [];
    Stack<FunctionStmtContext> _func = [];
    
    Dictionary<string, VariableInfo> _variables = (variables?.ToDictionary(v => v.Name)) ?? [];

    BlockStmtContext _lastBlockStatement;

    public void Write(StartRuleContext start)
    {
        var module = start.module();
        WriteModule(module);
    }


    void WriteModule(ModuleContext module)
    {
        var body = module.moduleBody();

        sb.StartBlock("class VB6 {");

        foreach (var element in body.moduleBodyElement()) {
            if (element.moduleBlock() is ModuleBlockContext block) {
                WriteBlock(block.block());
            }
            else if (element.subStmt() is SubStmtContext sub) {
                WriteSub(sub);
            }
            else if (element.functionStmt() is FunctionStmtContext func) {
                WriteFunc(func);
            }
            else if (element.enumerationStmt() is EnumerationStmtContext enums) {
                WriteEnum(enums);
            }
            else if (element.typeStmt() is TypeStmtContext type) {
                WriteType(type);
            }
            else if (element.declareStmt() is DeclareStmtContext declare) {
                WriteDeclare(declare);
            }
            else {
                NotSupported(element);
            }

            sb.AppendLine();
        }

        sb.EndBlock("}");
    }

    void WriteSub(SubStmtContext sub)
    {
        if (sub.visibility() is VisibilityContext visibility) {
            WriteVisibility(visibility);
        }

        WriteIdentifier(sub.ambiguousIdentifier());

        WriteArgList(sub.argList());

        sb.StartBlock($"{{");
        WriteBlock(sub.block());
        sb.EndBlock("}");
    }

    void WriteFunc(FunctionStmtContext func)
    {
        _func.Push(func);

        try {
            if (func.visibility() is VisibilityContext visibility) {
                WriteVisibility(visibility);
                Write(" ");
            }

            WriteAsTypeClause(func.asTypeClause());
            Write(" ");

            WriteIdentifier(func.ambiguousIdentifier());

            WriteArgList(func.argList());

            sb.StartBlock($"{{");
            WriteBlock(func.block());
            sb.EndBlock("}");
        }
        finally {
            _func.Pop();
        }
    }

    void WriteEnum(EnumerationStmtContext e)
    {
        if (e.publicPrivateVisibility() is PublicPrivateVisibilityContext visibility) {
            WriteVisibility(visibility);
        }

        sb.Append(" enum ");
        WriteIdentifier(e.ambiguousIdentifier());
        sb.StartBlock(" {");

        var constants = e.enumerationStmt_Constant().Select(c => AsString(() => {
            WriteIdentifier(c.ambiguousIdentifier());
            if (c.valueStmt() is ValueStmtContext value) {
                sb.Append(" = ");
                WriteValue(value);
            }
        }));

        sb.AppendLine(string.Join(Environment.NewLine, constants));

        sb.EndBlock("}");
    }

    void WriteType(TypeStmtContext type)
    {
        if (type.visibility() is VisibilityContext visibility) {
            WriteVisibility(visibility);
        }

        sb.Append(" struct ");
        WriteIdentifier(type.ambiguousIdentifier());
        sb.StartBlock(" {");

        foreach (var t in type.typeStmt_Element()) {
            sb.Append("public ");
            WriteAsTypeClause(t.asTypeClause());
            sb.Append(" ");
            WriteIdentifier(t.ambiguousIdentifier());
            sb.AppendLine(";");
        }

        sb.EndBlock("}");
    }

    void WriteDeclare(DeclareStmtContext declare)
    {
        Write("[DllImport(");
        Write(declare.STRINGLITERAL(0).GetText());
        Write(", EntryPoint = \"");
        WriteIdentifier(declare.ambiguousIdentifier());
        sb.AppendLine("\")]");

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
        sb.AppendLine(";");
    }

    

    void WriteBlock(BlockContext block)
    {
        foreach (var stmt in block.blockStmt()) {
            WriteBlockStmt(stmt);
        }
    }

    void WriteBlockStmt(BlockStmtContext stmt)
    {
        if (_lastBlockStatement != null) {
            if (stmt.Start.Line > _lastBlockStatement.Stop.Line + 1) {
                sb.AppendLine("");
            }
        }

        if (stmt.constStmt() is ConstStmtContext @const) {
            WriteConstants(@const);
        }
        else if (stmt.variableStmt() is VariableStmtContext var) {
            WriteVariables(var);
        }
        else if (stmt.onErrorStmt() is OnErrorStmtContext onError) {
            WriteOnError(onError);
        }
        else if (stmt.withStmt() is WithStmtContext block) {
            WriteWith(block);
        }
        //else if (stmt.implicitCallStmt_InBlock() is ImplicitCallStmt_InBlockContext implicitStmt) {
        //    WriteImplicitCall(implicitStmt);
        //}
        else if (stmt.letStmt() is LetStmtContext let) {
            WriteLet(let);
        }
        else if (stmt.setStmt() is SetStmtContext set) {
            WriteSet(set);
        }
        else if (stmt.ifThenElseStmt() is IfThenElseStmtContext ifthen) {
            WriteIfThenElse(ifthen);
        }
        else if (stmt.selectCaseStmt() is SelectCaseStmtContext select) {
            WriteSelect(select);
        }
        else if (stmt.doLoopStmt() is DoLoopStmtContext doloop) {
            WriteDoLoop(doloop);
        }
        else if (stmt.forNextStmt() is ForNextStmtContext forNext) {
            WriteForNext(forNext);
        }
        else if (stmt.loadStmt() is LoadStmtContext load) {
            sb.Append("// Load ");
            WriteValue(load.valueStmt());
        }
        else if (stmt.unloadStmt() is UnloadStmtContext unload) {
            sb.Append("// Unload ");
            WriteValue(unload.valueStmt());
        }
        else if (stmt.openStmt() is OpenStmtContext open) {
            WriteOpen(open);
        }
        else if (stmt.printStmt() is PrintStmtContext print) {
            WritePrint(print);
        }
        else if (stmt.closeStmt() is CloseStmtContext close) {
            WriteClose(close);
        }
        else if (stmt.eraseStmt() is EraseStmtContext erase) {
            WriteErase(erase);
        }
        else if (stmt.resumeStmt() is ResumeStmtContext resume) {
            WriteResume(resume);
        }
        else if (stmt.redimStmt() is RedimStmtContext redim) {
            WriteRedim(redim);
        }
        else if (stmt.goToStmt() is GoToStmtContext go) {
            sb.Append("goto ");
            WriteValue(go.valueStmt());
            sb.Append(";");
        }
        else if (stmt.exitStmt() is ExitStmtContext exit) {
            if (exit.EXIT_FUNCTION() != null || exit.EXIT_SUB() != null || exit.EXIT_PROPERTY != null) {
                sb.Append("return;");
            }
            else if (exit.EXIT_DO() != null || exit.EXIT_FOR() != null) {
                sb.Append("break;");
            }
        }
        else if (stmt.lineLabel() is LineLabelContext label) {
            WriteIdentifier(label.ambiguousIdentifier());
            Write(":");
        }
        else {
            NotSupported(stmt);
        }

        foreach (var comment in stmt.COMMENT()) {
            sb.Append(" ");
            WriteComment(comment);
        }

        if (!sb.IsLineStart) {
            sb.AppendLine();
        }

        _lastBlockStatement = stmt;
    }

    void WriteRedim(RedimStmtContext redim)
    {
        foreach (var stmt in redim.redimSubStmt()) {
            WriteImplicitCall(stmt.implicitCallStmt_InStmt());

            Write(" = new ");

            WriteAsTypeClause(stmt.asTypeClause());

            if (stmt.subscripts() is SubscriptsContext subscripts) {
                var subscriptList = subscripts.subscript();
                if (subscriptList.Length == 1) {
                    Write($"[{subscriptList[0].valueStmt(1).GetText()} + 1]");
                }
                else if (subscriptList.Length == 2) {
                    Write($"[{subscriptList[0].valueStmt(1).GetText()} + 1, {subscriptList[1].valueStmt(1).GetText()} + 1]");
                }
                else {
                    NotSupported(subscripts);
                }
            }

            Write(";");
        }
    }

    private void WriteForNext(ForNextStmtContext forNext)
    {
        Write("for (");

        if (forNext.asTypeClause() is AsTypeClauseContext asType) {
            WriteAsTypeClause(asType);
            Write(" ");
        }

        WriteVariableOrProcedureCall(forNext.iCS_S_VariableOrProcedureCall());
        Write(" = ");
        WriteValue(forNext.valueStmt(0));
        Write("; ");

        WriteValue(forNext.valueStmt(0));
        Write(" < ");
        WriteValue(forNext.valueStmt(1));
        Write("; ");

        WriteValue(forNext.valueStmt(0));

        if (forNext.valueStmt(2) is ValueStmtContext step) {
            Write(" += ");
            WriteValue(step);
        }
        else {
            Write("++");
        }

        sb.StartBlock(") {");
        WriteBlock(forNext.block());
        sb.EndBlock("}");

        if (forNext.typeHint().Length > 0) {
            NotSupported(forNext.typeHint()[0]);
        }
    }

    void WriteOpen(OpenStmtContext open)
    {
        Write("var ");
        WriteValue(open.valueStmt(1));
        Write(" = new StreamWriter(File.Open(");

        WriteValue(open.valueStmt(0));

        if (open.READ_WRITE() != null) {
            Write(", FileAccess.ReadWrite, ");
        }
        else if (open.READ() != null) {
            Write(", FileAccess.Read, ");
        }
        else if (open.WRITE() != null) {
            Write(", FileAccess.Write, ");
        }

        if (open.LOCK_READ_WRITE() != null) {
            Write(", FileShare.None");
        }
        else if (open.LOCK_WRITE() != null) {
            Write(", FileShare.Read");
        }
        else if (open.LOCK_READ() != null) {
            Write(", FileShare.Write");
        }
        else if (open.SHARED() != null) {
            Write(", FileShare.ReadWrite");
        }

        Write("));");
    }

    void WriteErase(EraseStmtContext erase)
    {
        foreach (var value in erase.valueStmt()) {
            Write("File.Delete(");
            WriteValue(value);
            Write(");");
            sb.AppendLine();
        }
    }

    void WritePrint(PrintStmtContext print)
    {
        WriteValue(print.valueStmt());
        Write(".Write(");

        if (print.outputList() is OutputListContext outputs) {
            foreach (var output in outputs.outputList_Expression()) {
                Write(", ");

                if (output.valueStmt() is ValueStmtContext value) {
                    WriteValue(value);
                }
                if (output.argsCall() is ArgsCallContext argsCall) {
                    WriteArgs(argsCall, false);
                }
                
            }
        }

        Write(")");
    }

    void WriteClose(CloseStmtContext close)
    {
        WriteValue(close.valueStmt(0));

        Write(".Dispose()");
    }


    void WriteConstants(ConstStmtContext @const)
    {
        bool isPublic = false;

        if (@const.publicPrivateGlobalVisibility() is PublicPrivateGlobalVisibilityContext visibility) {
            isPublic = visibility.PUBLIC() != null || visibility.GLOBAL() != null;
        }

        foreach (var sub in @const.constSubStmt()) {
            var variable = new VariableInfo {
                Type = AsString(() => WriteAsTypeClause(sub.asTypeClause())),
                Name = AsString(() => WriteIdentifier(sub.ambiguousIdentifier())),
                Const = true,
                Public = isPublic,
            };
            
            var value = AsString(() => WriteValue(sub.valueStmt()));

            sb.AppendLine($"{(isPublic ? "public" : "private")} const {variable.Type} {variable.Name} = {value};");

            _variables.Add(variable.Name, variable);
        }
    }

    void WriteVariables(VariableStmtContext var)
    {
        var visibility = var.visibility();

        var list = var.variableListStmt();

        foreach (var sub in list.variableSubStmt()) {
            var info = new VariableInfo {
                Name = AsString(() => WriteIdentifier(sub.ambiguousIdentifier())),
                Type = AsString(() => WriteAsTypeClause(sub.asTypeClause()))
            };

            string array = string.Empty;
            string init = string.Empty;

            if (sub.subscripts() is SubscriptsContext subscripts) {
                info.IsArray = true;

                var subscriptList = subscripts.subscript();
                if (subscriptList.Length == 1) {
                    array = "[]";
                    init = $" = new {info.Type}[{subscriptList[0].GetText()} + 1]";
                }
                else if (subscriptList.Length == 2) {
                    array = "[,]";
                    init = $" = new {info.Type}[{int.Parse(subscriptList[0].valueStmt(1).GetText()) + 1}, {int.Parse(subscriptList[1].valueStmt(1).GetText()) + 1}]";
                }
                else {
                    NotSupported(subscripts);
                }
            }

            _variables.TryAdd(info.Name, info);

            sb.AppendLine($"{info.Type}{array} {info.Name}{init};");
        }
    }

    void WriteResume(ResumeStmtContext resume)
    {
        if (resume.INTEGERLITERAL() is ITerminalNode literal) {
            sb.Append($"// goto {literal.GetText()};");
        }
        else if (resume.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
            sb.Append("goto ");
            WriteIdentifier(resume.ambiguousIdentifier());
            sb.Append(";");
        }
        else if (resume.NEXT() is ITerminalNode terminal) {
            return;
        }
        else {
            NotSupported(resume);
        }
    }

    void WriteAsTypeClause(AsTypeClauseContext asType)
    {
        if (asType is null) {
            sb.Append("object");
            return;
        }

        if (asType.fieldLength() is FieldLengthContext field) {
            NotSupported(field);
        }

        sb.Append(ConvertType(asType.type()));
    }

    private void WriteDoLoop(DoLoopStmtContext doloop)
    {
        sb.Append("while (");

        if (doloop.valueStmt() is ValueStmtContext val) {
            WriteValue(val);
        }
        else {
            Write("true");
        }

        sb.StartBlock(") {");

        WriteBlock(doloop.block());

        sb.EndBlock("}");
    }

    private void WriteSelect(SelectCaseStmtContext select)
    {
        sb.Append("switch (");
        WriteValue(select.valueStmt());
        sb.StartBlock(") {");

        foreach (var cs in select.sC_Case()) {
            sb.Append("case ");
            var condition = cs.sC_Cond();
            sb.Append(condition.GetText());
            sb.Append(":");

            if (cs.block() is BlockContext block) {
                sb.StartBlock("");
                WriteBlock(block);
                sb.EndBlock("");
            }

        }

        sb.EndBlock("}");
    }

    private void WriteComment(ITerminalNode comment)
    {
        sb.Append("//");
        sb.Append(comment.GetText().TrimStart().TrimStart('\''));
    }

    private void WriteSet(SetStmtContext set)
    {
        WriteImplicitCall(set.implicitCallStmt_InStmt());
        sb.Append(" = ");
        WriteValue(set.valueStmt());
        sb.Append(";");
    }

    void WriteIfThenElse(IfThenElseStmtContext ifthen)
    {
        if (ifthen is BlockIfThenElseContext @if) {
            if (@if.ifBlockStmt() is IfBlockStmtContext ifBlock) {
                var condition = AsString(() => WriteValue(ifBlock.ifConditionStmt().valueStmt()));

                sb.StartBlock($"if ({condition}) {{");
                WriteBlock(ifBlock.block());
                sb.EndBlock("}");
            }

            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var elseIfCondition = AsString(() => WriteValue(elseif.ifConditionStmt().valueStmt()));
                    sb.StartBlock($"else if ({elseIfCondition}) {{");
                    WriteBlock(elseif.block());
                    sb.EndBlock("}");
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                sb.StartBlock($"else {{");
                WriteBlock(@else.block());
                sb.EndBlock("}");
            }
        }
        else if (ifthen is InlineIfThenElseContext inline) {
            if (inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
                var condition = AsString(() => WriteValue(ifcond.valueStmt()));

                sb.StartBlock($"if ({condition}) {{");
                WriteBlockStmt(inline.blockStmt(0));
                sb.EndBlock("}");

                if (inline.ELSE() is ITerminalNode terminal) {
                    sb.StartBlock("else {{");
                    WriteBlockStmt(inline.blockStmt(1));
                    sb.EndBlock("}}");
                }
            }
        }
        else {
            NotSupported(ifthen);
        }
    }


    void WriteImplicitCall(ImplicitCallStmt_InBlockContext callInBlock)
    {
        if (callInBlock.iCS_B_MemberProcedureCall() is ICS_B_MemberProcedureCallContext member) {
            string callee;
            if (member.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext callInStmt) {
                callee = AsString(() => WriteImplicitCall(callInStmt));
            }
            else if (_with.TryPeek(out var with)) {
                callee = AsString(() => WriteImplicitCall(with));
            }
            else {
                throw new InvalidOperationException("Member procedure call without callee.");
            }

            string procedure;
            if (member.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
                procedure = identifier.GetText();
            }
            else {
                throw new InvalidOperationException("Member procedure call without identifier.");
            }

            string arguments;
            if (member.argsCall() is ArgsCallContext args) {
                arguments = AsString(() => WriteArgs(args, false));
            }
            else {
                arguments = "()";
            }

            if (member.dictionaryCallStmt() is DictionaryCallStmtContext dic) {
                NotSupported(dic);
            }

            sb.Append($"{callee}.{procedure}{arguments};");
        }
        else if (callInBlock.iCS_B_ProcedureCall() is ICS_B_ProcedureCallContext procedure) {
            string arguments = string.Empty;

            if (procedure.argsCall() is ArgsCallContext args) {
                arguments = AsString(() => WriteArgs(args, false));
            }

            if (procedure.certainIdentifier() is CertainIdentifierContext certain) {
                sb.Append($"{certain.GetText()}{arguments}");
            }
            else {
                NotSupported(procedure);
            }
        }
        else {
            NotSupported(callInBlock);
        }

    }

    void WriteImplicitCall(ImplicitCallStmt_InStmtContext callInStatement)
    {
        if (callInStatement.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varOrProc) {
            WriteVariableOrProcedureCall(varOrProc);
        }
        else if (callInStatement.iCS_S_MembersCall() is ICS_S_MembersCallContext membersCall) {
            WriteMembersCall(membersCall);
        }
        else if (callInStatement.iCS_S_DictionaryCall() is ICS_S_DictionaryCallContext dictionaryCall) {
            if (_with.TryPeek(out var with)) {
                WriteImplicitCall(with);
            }
            WriteDictionaryCall(dictionaryCall);
        }
        else if (callInStatement.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext procedureArray) {
            WriteProcedureArray(procedureArray);
        }
        else {
            NotSupported(callInStatement);
        }
    }

    void WriteMembersCall(ICS_S_MembersCallContext membersCall)
    {
        //if (membersCall.dictionaryCallStmt() is DictionaryCallStmtContext dict) {
        //    NotSupported(dict);
        //}
        //


        if (membersCall.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varOrProc) {
            WriteVariableOrProcedureCall(varOrProc);
        }
        else if (membersCall.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext proc) {
            WriteProcedureArray(proc);
        }
        else if (_with.TryPeek(out var with)) {
            WriteImplicitCall(with);
        }
        else {
            NotSupported(membersCall);
        }

        if (membersCall.iCS_S_MemberCall() is ICS_S_MemberCallContext[] members && members.Length > 0) {
            foreach (var member in members) {
                sb.Append(".");
                WriteMemberCall(member);
            }
        }
    }

    void WriteMemberCall(ICS_S_MemberCallContext memberCall)
    {
        if (memberCall.iCS_S_VariableOrProcedureCall() is ICS_S_VariableOrProcedureCallContext varOrProc) {
            WriteVariableOrProcedureCall(varOrProc);
        }
        else if (memberCall.iCS_S_ProcedureOrArrayCall() is ICS_S_ProcedureOrArrayCallContext proc) {
            WriteProcedureArray(proc);
        }
        else {
            NotSupported(memberCall);
        }
    }

    void WriteProcedureArray(ICS_S_ProcedureOrArrayCallContext procedureArray)
    {
        string name = WriteIdentifier(procedureArray.ambiguousIdentifier());

        var variable = _variables.TryGetValue(name, out var info) ? info : null;

        if (procedureArray.baseType() != null) {
            NotSupported(procedureArray.baseType());
        }


        //if (procedureArray.typeHint() is TypeHintContext hint) {
        //    NotSupported(procedureArray.typeHint());
        //}

        foreach (var args in procedureArray.argsCall()) {
            WriteArgs(args, variable?.IsArray == true);
        }
    }

    void WriteVariableOrProcedureCall(ICS_S_VariableOrProcedureCallContext varOrProc)
    {
        if (varOrProc.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
            WriteIdentifier(identifier);

            if (varOrProc.dictionaryCallStmt() is DictionaryCallStmtContext dictCall) {
                WriteDictionaryCall(dictCall);
            }
        }
        else {
            NotSupported(varOrProc);
        }
    }



    void WriteDictionaryCall(ICS_S_DictionaryCallContext dictionaryCall)
    {
        var stmt = dictionaryCall.dictionaryCallStmt();
        WriteDictionaryCall(stmt);
    }

    void WriteDictionaryCall(DictionaryCallStmtContext stmt)
    {
        sb.Append("[\"");
        WriteIdentifier(stmt.ambiguousIdentifier());
        sb.Append("\"]");
    }

    string WriteIdentifier(AmbiguousIdentifierContext identifier)
    {
        if (identifier is null) {
            throw new Exception("Expected identifier.");
        }

        string text = identifier.GetText();

        if (text == "vbNullString") {
            sb.Append("string.Empty");
        }
        else {
            sb.Append(text);
        }

        return text;
    }



    void WriteWith(WithStmtContext with)
    {
        sb.StartBlock("{");

        _with.Push(with.implicitCallStmt_InStmt());
        WriteBlock(with.block());
        _with.Pop();

        sb.EndBlock("}");
    }

    void WriteOnError(OnErrorStmtContext var)
    {
        sb.AppendLine($"// {var.GetText()}");
    }

    void WriteLet(LetStmtContext let)
    {
        string target;

        if (let.implicitCallStmt_InStmt() is ImplicitCallStmt_InStmtContext call) {
            target = AsString(() => WriteImplicitCall(call));
        }
        else {
            throw NotSupported(let);
        }

        if (_func.TryPeek(out FunctionStmtContext func)
            && string.Equals(func.ambiguousIdentifier().GetText(), target)) {
            Write("return ");
        }
        else {
            Write(target);
            sb.Append(" = ");
        }


        if (let.valueStmt() is ValueStmtContext value) {
            WriteValue(value);
        }
        else {
            NotSupported(let);
        }

        sb.Append(";");
    }

    void WriteArgs(ArgsCallContext args, bool dictionary)
    {
        string GetArg(ArgCallContext arg) => AsString(() => WriteValue(arg.valueStmt()));
        string[] arguments = args.argCall().Select(GetArg).ToArray();

        if (dictionary) {
            sb.Append($"[{string.Join(", ", arguments)}]");
        }
        else {
            sb.Append($"({string.Join(", ", arguments)})");
        }
    }

    void WriteValue(ValueStmtContext value)
    {
        if (value is VsICSContext vsics) {
            WriteImplicitCall(vsics.implicitCallStmt_InStmt());
        }
        else if (value is VsLiteralContext literal) {
            WriteLiteral(literal.literal());
        }
        else if (value is VsAssignContext assign) {
            WriteImplicitCall(assign.implicitCallStmt_InStmt());
            sb.Append(": ");
            WriteValue(assign.valueStmt());
        }
        else if (value is VsNotContext not) {
            sb.Append("!");
            WriteValue(not.valueStmt());
        }
        else if (value is VsStructContext vsstruct) {
            WriteValueList(vsstruct.valueStmt(), ", ");
        }
        else if (value is VsAmpContext amp) {
            WriteValueList(amp.valueStmt(), " + ");
        }
        else if (value is VsEqContext eq) {
            WriteValueList(eq.valueStmt(), " == ");
        }
        else if (value is VsNeqContext neq) {
            WriteValueList(neq.valueStmt(), " != ");
        }
        else if (value is VsGeqContext geq) {
            WriteValueList(geq.valueStmt(), " >= ");
        }
        else if (value is VsGtContext gt) {
            WriteValueList(gt.valueStmt(), " > ");
        }
        else if (value is VsLeqContext leq) {
            WriteValueList(leq.valueStmt(), " <= ");
        }
        else if (value is VsLtContext lt) {
            WriteValueList(lt.valueStmt(), " < ");
        }
        else if (value is VsAddContext add) {
            WriteValueList(add.valueStmt(), " + ");
        }
        else if (value is VsMinusContext min) {
            WriteValueList(min.valueStmt(), " - ");
        }
        else if (value is VsMultContext mul) {
            WriteValueList(mul.valueStmt(), " * ");
        }
        else if (value is VsDivContext div) {
            WriteValueList(div.valueStmt(), " / ");
        }
        else if (value is VsOrContext or) {
            WriteValueList(or.valueStmt(), " || ");
        }
        else if (value is VsAndContext and) {
            WriteValueList(and.valueStmt(), " && ");
        }
        else if (value is VsXorContext xor) {
            WriteValueList(xor.valueStmt(), " ^ ");
        }
        else {
            NotSupported(value);
        }

        void WriteValueList(ValueStmtContext[] values, string separator)
        {
            var strings = values.Select(v => AsString(() => WriteValue(v))).ToArray();
            sb.Append(string.Join(separator, strings));
        }
    }

    void WriteLiteral(LiteralContext lit)
    {
        if (lit.COLORLITERAL() is ITerminalNode color) {
            var hex = "0x" + color.Symbol.Text[2..].TrimEnd(['&']).PadLeft(6, '0').PadLeft(8, 'F');

            var col = System.Drawing.Color.FromArgb(System.Convert.ToInt32(hex, 16));

            if (col.IsNamedColor) {
                sb.Append($"Color.FromName(\"{col.Name}\")");
            }
            else if (col.A == 255) {
                sb.Append($"Color.FromArgb({col.R}, {col.G}, {col.B})");
            }
            else {
                sb.Append($"Color.FromArgb({col.A}, {col.R}, {col.G}, {col.B})");
            }
        }
        else if (lit.FILENUMBER() is ITerminalNode file) {
            sb.Append(file.GetText().TrimStart('#'));
        }
        else if (lit.TRUE() is ITerminalNode @true) {
            sb.Append("true");
        }
        else if (lit.FALSE() is ITerminalNode @false) {
            sb.Append("false");
        }
        else if (lit.NOTHING() is ITerminalNode @nothing || lit.NULL() is ITerminalNode @null) {
            sb.Append("null");
        }
        else if (lit.INTEGERLITERAL() is ITerminalNode @short) {
            sb.Append(@short.GetText());
        }
        else if (lit.DOUBLELITERAL() is ITerminalNode @double) {
            sb.Append(@double.GetText().TrimEnd(['&']));
        }
        else {
            sb.Append(lit.GetText());
        }
    }


    void WriteVisibility(PublicPrivateVisibilityContext v)
    {
        if (v.PRIVATE() != null) {
            sb.Append("private");
        }
        else if (v.PUBLIC() != null) {
            sb.Append("public");
        }
        else {
            NotSupported(v);
        }
    }

    void WriteVisibility(VisibilityContext v)
    {
        if (v.PRIVATE() != null) {
            sb.Append("private");
        }
        else if (v.PUBLIC() != null || v.GLOBAL() != null) {
            sb.Append("public");
        }
        else if (v.FRIEND() != null) {
            sb.Append("internal");
        }
        else {
            NotSupported(v);
        }
    }

    void WriteArgList(ArgListContext args)
    {
        Write("(");
        Write(string.Join(", ", args.arg().Select(a => AsString(() => {
            WriteAsTypeClause(a.asTypeClause());
            Write(" ");
            WriteIdentifier(a.ambiguousIdentifier());
            if (a.argDefaultValue() is ArgDefaultValueContext def) {
                Write(" = ");
                WriteValue(def.valueStmt());
            }
        }))));

        Write(")");
    }


    static string ConvertType(TypeContext type)
    {
        var complexType = type.complexType();
        if (complexType != null) {
            return type.GetText();
        }

        var baseType = type.baseType();
        var typeSymbol = ((ITerminalNode)baseType.GetChild(0)).Symbol;
        return typeSymbol.Type switch {
            BOOLEAN => "bool",
            BYTE => "byte",
            COLLECTION => "System.Collections.ICollection",
            DATE => "DateTime",
            DOUBLE => "double",
            INTEGER => "short",
            LONG => "int",
            OBJECT => "object",
            SINGLE => "float",
            STRING => "string",
            VARIANT => "object",
            _ => typeSymbol.Text
        };
    }

    static string ConvertArg(ArgContext arg)
    {
        
        return arg.ambiguousIdentifier().GetText();
    }


    [DebuggerHidden]
    static Exception NotSupported(ParserRuleContext context, [CallerMemberName] string caller = null)
    {
        throw new NotSupportedException($"Not supported from {caller}: '{context.GetText()}'\r\n{new ConsoleVisitor().Visit(context)}");
    }

    void Write(string str) => sb.Append(str);

    string AsString(Action act)
    {
        var last = sb;
        sb = new StatementBuilder();
        try {
            act();
            return sb.ToString();
        }
        finally {
            sb = last;
        }
    }
}
