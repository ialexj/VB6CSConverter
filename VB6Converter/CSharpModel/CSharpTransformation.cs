using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.CSharpModel;

public class CSharpTransformation
{
    readonly Stack<ImplicitCallStmt_InStmtContext> _with = [];


    public CompilationUnitSyntax GetCompilationUnit(ModuleContext module, string name, string ns, VisualBasicFileType type = VisualBasicFileType.Module)
    {
        using var _ = new TraceMethod(module);

        var cu = CompilationUnit()
            .AddUsings(
                UsingDirective(ParseName("System")),
                UsingDirective(ParseName("System.Collections.Generic")));

        var @namespace = FileScopedNamespaceDeclaration(IdentifierName(ns ?? name));

        var body = module.moduleBody();
        @namespace = @namespace
            .WithMembers(
                SingletonList<MemberDeclarationSyntax>(
                    GetClass(body, name,
                        @static: type == VisualBasicFileType.Module)));

        return cu.WithMembers(SingletonList<MemberDeclarationSyntax>(@namespace)).NormalizeWhitespace();
            
    }
    
    public ClassDeclarationSyntax GetClass(ModuleBodyContext body, string name, bool @static = false)
    {
        using var _ = new TraceMethod(body);

        IEnumerable<SyntaxToken> GetModifiers()
        {
            yield return Token(SyntaxKind.PublicKeyword);
            yield return Token(SyntaxKind.PartialKeyword);
            if (@static) {
                yield return Token(SyntaxKind.StaticKeyword);
            }
        }

        var c = ClassDeclaration(name)
            .WithModifiers(TokenList(GetModifiers()));

        foreach (var e in body.moduleBodyElement()) {
            if (e.moduleBlock() is ModuleBlockContext moduleBlock) {
                if (moduleBlock.block() is BlockContext block) {
                    foreach (var stmt in block.blockStmt()) {
                        if (stmt.constStmt() is ConstStmtContext @const) {
                            c = c.AddMembers(GetConstantFields(@const).ToArray());
                        }
                        else if (stmt.variableStmt() is VariableStmtContext var) {
                            c = c.AddMembers(GetVariableFields(var).ToArray());
                        }
                        else {
                            NotSupported(stmt);
                        }
                    }
                }
            }

            else if (e.enumerationStmt() is EnumerationStmtContext enumCtx) {
                c = c.AddMembers(GetEnum(enumCtx));
            }
            else if (e.typeStmt() is TypeStmtContext typeCtx) {
                c = c.AddMembers(GetStruct(typeCtx));
            }

            else if (e.subStmt() is SubStmtContext sub) {
                c = c.AddMembers(GetMethod(sub));
            }
            else if (e.functionStmt() is FunctionStmtContext func) {
                c = c.AddMembers(GetMethod(func));
            }
            else if (e.declareStmt() is DeclareStmtContext declare) {
                c = c.AddMembers(GetExtern(declare));
            }

            else if (e.propertyAccessor() is IPropertyContext prop) {
                var property = GetProperty(prop);
                var existing = c.Members.OfType<PropertyDeclarationSyntax>().FirstOrDefault(p => Equals(p.Identifier.Text, property.Identifier.Text));
                if (existing != null) {
                    var replace = existing.AddAccessorListAccessors([.. property.AccessorList.Accessors]);
                    c = c.ReplaceNode(existing, replace);
                }
                else {
                    c = c.AddMembers(property);
                }
            }

            else {
                NotSupported(e);
            }
        }

        return c;

        StructDeclarationSyntax GetStruct(TypeStmtContext type)
        {
            using var _ = new TraceMethod(type);

            return StructDeclaration(GetIdentifier(type.ambiguousIdentifier()))
                .WithModifiers(TokenList(Token(GetVisibilityKeyword(type.visibility()))))
                .WithMembers(
                    List<MemberDeclarationSyntax>(
                        type.typeStmt_Element().Select(e =>
                            FieldDeclaration(
                                VariableDeclaration(GetTypeRoslyn(e.asTypeClause()))
                                    .WithVariables(
                                        SingletonSeparatedList(
                                            VariableDeclarator(GetIdentifierToken(e.ambiguousIdentifier())))))
                            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                        )
                    )
                );
        
        }

        EnumDeclarationSyntax GetEnum(EnumerationStmtContext e)
        {
            using var _ = new TraceMethod(e);

            return EnumDeclaration(GetIdentifier(e.ambiguousIdentifier()))
                .WithModifiers(TokenList(Token(GetVisibilityKeyword(e.publicPrivateVisibility()))))
                .WithMembers(
                    SeparatedList<EnumMemberDeclarationSyntax>(
                        e.enumerationStmt_Constant().Select(c => {
                            var m = EnumMemberDeclaration(Identifier(GetIdentifier(c.ambiguousIdentifier())));
                            if (c.valueStmt() is ValueStmtContext v) {
                                m = m.WithEqualsValue(EqualsValueClause(GetValueSyntax(v)));
                            }
                            return m;
                        }))
                    );
        }

        MethodDeclarationSyntax GetMethod(IMethodContext methodCtx)
        {
            using var _ = new TraceMethod(methodCtx);

            var type = GetTypeRoslyn(methodCtx.asTypeClause());
            var name = GetIdentifier(methodCtx.ambiguousIdentifier());
            var visb = GetVisibilityKeyword(methodCtx.visibility());
            var args = GetMethodArguments(methodCtx.argList());
            var body = GetBlockRoslyn(methodCtx.block());

            // Rewrite return style
            body = ReturnValueRewriter.RewriteMethodReturn(type, name, body);

            return MethodDeclaration(type, name)
                .WithModifiers(TokenList(new SyntaxToken?[] {
                    Token(visb),
                    @static ? Token(SyntaxKind.StaticKeyword) : null
                }.OfType<SyntaxToken>()))
                .WithParameterList(GetMethodParameters(methodCtx.argList()))
                .WithBody(body);
        }

        PropertyDeclarationSyntax GetProperty(IPropertyContext propCtx)
        {
            var vis = GetVisibilityKeyword(propCtx.visibility());
            var name = GetIdentifier(propCtx.ambiguousIdentifier());
            var body = GetBlockRoslyn(propCtx.block());

            SyntaxKind kind;
            TypeSyntax type;

            if (propCtx is PropertyGetStmtContext get) {
                kind = SyntaxKind.GetAccessorDeclaration;
                type = GetTypeRoslyn(get.asTypeClause());
                body = ReturnValueRewriter.RewriteMethodReturn(type, name, body);
            }
            else if (propCtx is IPropertySetContext set) {
                kind = SyntaxKind.SetAccessorDeclaration;

                var parameters = GetMethodParameters(set.argList());
                if (parameters.Parameters.Count != 1) {
                    throw NotSupported(set, "Expected one parameter for setter.");
                }

                type = parameters.Parameters[0].Type;

                var identifier = parameters.Parameters[0].Identifier.ValueText;
                if (!Equals(identifier, "value")) {
                    var renamer = new SimpleIdentifierRenamer(identifier, "value");
                    body = (BlockSyntax)renamer.Visit(body);
                }
            }
            else {
                throw NotSupported(propCtx);
            }

            return PropertyDeclaration(type, name)
                .WithModifiers(TokenList(new SyntaxToken?[] {
                    Token(vis),
                    @static || propCtx.STATIC() is not null ? Token(SyntaxKind.StaticKeyword) : null,
                }.OfType<SyntaxToken>()))
                .WithAccessorList(AccessorList(
                    SingletonList(AccessorDeclaration(kind)
                        .WithBody(body))));
        }
        
        MethodDeclarationSyntax GetExtern(DeclareStmtContext declare)
        {
            using var _ = new TraceMethod(declare);

            IEnumerable<AttributeArgumentSyntax> GetAttributeArguments() {
                string library = declare.STRINGLITERAL(0).GetText().Trim('"');
                yield return AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(library)));
                
                if (declare.STRINGLITERAL(1) is ITerminalNode aliasNode) {
                    string alias = aliasNode.GetText().Trim('"');
                    yield return AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(alias)))
                        .WithNameEquals(NameEquals("EntryPoint"));
                }
            }

            return MethodDeclaration(
                GetTypeRoslyn(declare.asTypeClause()),
                GetIdentifier(declare.ambiguousIdentifier()))
                .WithModifiers(TokenList(
                    Token(GetVisibilityKeyword(declare.visibility())),
                    Token(SyntaxKind.StaticKeyword),
                    Token(SyntaxKind.ExternKeyword)
                ))
                .WithParameterList(GetMethodParameters(declare.argList()))
                .WithAttributeLists(SingletonList(AttributeList(SingletonSeparatedList(
                    Attribute(IdentifierName("DllImport"))
                        .WithArgumentList(AttributeArgumentList(SeparatedList(GetAttributeArguments())))
                ))))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        }

        ParameterListSyntax GetMethodParameters(ArgListContext argsContext)
        {
            using var _ = new TraceMethod(argsContext);

            ParameterSyntax GetParameter(ArgContext arg)
            {
                var parameter = Parameter(Identifier(GetIdentifier(arg.ambiguousIdentifier())))
                    .WithType(GetTypeRoslyn(arg.asTypeClause()));

                if (arg.argDefaultValue() is ArgDefaultValueContext def) {
                    parameter = parameter.WithDefault(EqualsValueClause(GetValueSyntax(def.valueStmt())));
                }

                return parameter;
            }

            return ParameterList(
                SeparatedList(
                    argsContext.arg().Select(GetParameter).ToArray()));
        }
        
        IEnumerable<FieldDeclarationSyntax> GetConstantFields(ConstStmtContext @const)
            => GetConstantDeclarations(@const).Select(v => FieldDeclaration(v)
                .WithModifiers(TokenList(
                    Token(GetVisibilityKeyword(@const.publicPrivateGlobalVisibility())),
                    Token(SyntaxKind.ConstKeyword))));
        
        IEnumerable<FieldDeclarationSyntax> GetVariableFields(VariableStmtContext var)
            => GetVariableDeclarations(var).Select(v => FieldDeclaration(v)
                .WithModifiers(TokenList(
                    Token(GetVisibilityKeyword(var.visibility())),
                    Token(SyntaxKind.StaticKeyword)
                )));
    }


    IEnumerable<VariableDeclarationSyntax> GetConstantDeclarations(ConstStmtContext @const)
    {
        using var _ = new TraceMethod(@const);

        return @const.constSubStmt().Select(sub => VariableDeclaration(GetTypeRoslyn(sub.asTypeClause()))
            .WithVariables(
                SingletonSeparatedList(
                    VariableDeclarator(Identifier(GetIdentifier(sub.ambiguousIdentifier())))
                        .WithInitializer(EqualsValueClause(GetValueSyntax(sub.valueStmt())))
                )
            ));
    }

    IEnumerable<VariableDeclarationSyntax> GetVariableDeclarations(VariableStmtContext var)
    {
        using var _ = new TraceMethod(var);

        foreach (var sub in var.variableListStmt().variableSubStmt()) {

            var name = GetIdentifier(sub.ambiguousIdentifier());

            bool isArray = sub.LPAREN() is not null && sub.RPAREN() is not null;

            int arraySizeCounts = Math.Max(isArray ? 1 : 0, sub.subscripts()?.subscript().Length ?? 0);
            var omittedExpressions = Enumerable.Range(0, arraySizeCounts)
                .Select(_ => (SyntaxNodeOrToken)OmittedArraySizeExpression())
                .Intersperse(Token(SyntaxKind.CommaToken))
                .ToArray();

            List<string> arrayDimensions = [];

            if (sub.subscripts() is SubscriptsContext subscripts) {
                foreach (var subscript in subscripts.subscript()) {
                    var from = GetValue(subscript.valueStmt(0));
                    var to = subscript.valueStmt(1) is ValueStmtContext v ? GetValue(v) : null;
                    arrayDimensions.Add($"{to} + 1");
                }
            }

            var baseType = GetTypeRoslyn(sub.asTypeClause());
            var type = isArray
                ? ArrayType(baseType, SingletonList(ArrayRankSpecifier(SeparatedList<ExpressionSyntax>(omittedExpressions))))
                : baseType;

            var variable = VariableDeclarator(Identifier(name));
            if (arrayDimensions.Count > 0) {
                variable = variable.WithInitializer(EqualsValueClause(
                    ArrayCreationExpression(((ArrayTypeSyntax)type)
                        .WithRankSpecifiers(SingletonList(
                            ArrayRankSpecifier(SeparatedList(arrayDimensions.Select(i => ParseExpression(i)))
                        ))
                    ))
                ));
            }

            yield return VariableDeclaration(type).WithVariables(SingletonSeparatedList(variable));
        }
    }


    // Statements

    BlockSyntax GetBlockRoslyn(BlockContext block)
    {
        using var _ = new TraceMethod(block);

        if (block is null)
            return Block();

        return Block().WithStatements(List(block.blockStmt().SelectMany(GetStatement)));
    }

    IEnumerable<StatementSyntax> GetStatement(BlockStmtContext stmt)
    {
        using var _ = new TraceMethod(stmt);

        if (stmt.call() is ICallContext call) {
            return [ GetCallStatement(call) ];
        }
        else if (stmt.assignment() is IAssignmentContext assignment) {
            return [ ExpressionStatement(GetAssignment(assignment)) ];
        }
        else if (stmt.withStmt() is WithStmtContext with) {
            return [ GetWith(with) ];
        }
        else if (stmt.ifThenElseStmt() is IfThenElseStmtContext ifthen) {
            return [ GetIf(ifthen) ];
        }
        else {
            return GetBlockStatements(stmt).Select(s => ParseStatement(s.ToString()));
        }
    }


    CSharpBlockStatement GetBlock(BlockContext blockCtx)
    {
        using var _ = new TraceMethod(blockCtx);

        if (blockCtx is null) {
            return CSharpBlockStatement.Empty;
        }

        var block = new CSharpBlockStatement();

        foreach (var stmt in blockCtx.blockStmt()) {
            block.Statements.AddRange(GetBlockStatements(stmt));
        }

        return block;
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
            return [new CSharpReturnStatement()];
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


    StatementSyntax GetWith(WithStmtContext with)
    {
        using var _ = new TraceMethod(with);

        _with.Push(with.implicitCallStmt_InStmt());
        try {
            var block = GetBlockRoslyn(with.block());

            if (block.Statements.Count == 0) {
                return EmptyStatement();
            }
            else if (block.Statements.Count == 1) {
                return block.Statements[0];
            }
            else {
                return GetBlockRoslyn(with.block());
            }
        }
        finally {
            _with.Pop();
        }
    }

    ExpressionStatementSyntax GetCallStatement(ICallContext call)
    {
        using var _ = new TraceMethod(call);
        var expression = GetCallInvocationExpression(call);
        return ExpressionStatement(expression);
    }

    ExpressionSyntax GetCallInvocationExpression(ICallContext call)
    {
        using var _ = new TraceMethod(call);

        var expression = GetCallIdentifierExpression(call);

        var invocation = call.segments().LastOrDefault();
        var args = invocation?.args().FirstOrDefault();

        if (invocation is ICS_S_ProcedureOrArrayCallContext 
            || invocation is ECS_ProcedureCallContext
            || invocation is ECS_MemberProcedureCallContext
            || invocation is ICS_B_ProcedureCallContext 
            || invocation is ICS_B_MemberProcedureCallContext 
            || args?.ChildCount > 0) {
            return InvocationExpression(expression).WithArgumentList(GetArgumentList(args));
        }
        else {
            return expression;
        }
    }

    ExpressionSyntax GetCallIdentifierExpression(ICallContext call, bool preferArray = false)
    {
        using var _ = new TraceMethod(call);

        var segments = call.segments();
        if (call.IsPartial && _with.TryPeek(out var with)) {
            segments = with.segments().Concat(segments);
        }

        ExpressionSyntax expr = null;

        var invocation = segments.LastOrDefault();
        foreach (var s in segments) {
            var id = GetIdentifierNameToken(s.identifier());

            if (expr is not null) {
                expr = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expr, id);
            }
            else {
                expr = id;
            }

            if (s is ICS_S_ProcedureOrArrayCallContext procOrArray && procOrArray.argsCall().Length > 0 && (preferArray || s != invocation)) {
                expr = ElementAccessExpression(expr, GetBracketedArgumentList(s.args().FirstOrDefault()));
            }
        }

        if (call.dictionaryCallStmt() is DictionaryCallStmtContext dictCall) {
            var text = dictCall.ambiguousIdentifier().GetText();

            expr = ElementAccessExpression(expr, BracketedArgumentList(
                SingletonSeparatedList(
                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text)))
                )
            ));
        }

        return expr;
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
                    var to = subscript.valueStmt(1) is ValueStmtContext v ? GetValue(v) : null;
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


    AssignmentExpressionSyntax GetAssignment(IAssignmentContext assignment)
    {
        using var _ = new TraceMethod(assignment);
        var identifier = GetCallIdentifierExpression(assignment.implicitCallStmt_InStmt(), true);
        var value = GetValueSyntax(assignment.valueStmt());
        return AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, identifier, value);
    }

    IfStatementSyntax GetIf(IfThenElseStmtContext ifthen)
    {
        using var _ = new TraceMethod(ifthen);

        IfStatementSyntax root = null;
        IfStatementSyntax current = null;

        if (ifthen is BlockIfThenElseContext @if) {
            if (@if.ifBlockStmt() is IfBlockStmtContext ifBlock) {
                var condition = GetValueSyntax(ifBlock.ifConditionStmt().valueStmt());
                var then = GetBlockRoslyn(ifBlock.block());

                root = IfStatement(condition, then);
                current = root;
            }

            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var condition = GetValueSyntax(elseif.ifConditionStmt().valueStmt());
                    var then = GetBlockRoslyn(elseif.block());

                    var next = IfStatement(condition, then);
                    current = current.WithElse(ElseClause(next));
                    current = next;
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                var block = GetBlockRoslyn(@else.block());
                current = current.WithElse(ElseClause(block));
            }
        }
        else if (ifthen is InlineIfThenElseContext inline && inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
            var condition = GetValueSyntax(ifcond.valueStmt());
            var statements = GetStatement(inline.blockStmt(0)).ToArray();
            var statement = statements.Length == 1 ? statements[0] : Block(statements);

            root = IfStatement(condition, statement);

            if (inline.blockStmt(1) is BlockStmtContext elseBlock) {
                var elseStatements = GetStatement(elseBlock).ToArray();
                var elseStatement = elseStatements.Length == 1 ? elseStatements[0] : Block(elseStatements);
                root = root.WithElse(ElseClause(elseStatement));
            }
        }
        else {
            NotSupported(ifthen);
        }

        return root;
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
            Statements = new(GetBlock(forEach.block()))
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
    CSharpCallExpression GetVariableOrProcedureCall(ICallTargetContext proc, CallType type = CallType.Unspecified)
    {
        using var _ = new TraceMethod(proc);        
        var target = GetIdentifierReference(proc.identifier(), proc.typeHint());
        return GetCall(proc, target, null, type);
    }   

    CSharpCallExpression GetMembersCallExpression(ICS_S_MembersCallContext membersCall, CallType type = CallType.Unspecified)
    {
        using var _ = new TraceMethod(membersCall);

        throw NotSupported(membersCall);
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

    CSharpCallExpression GetCall(ICallTargetContext proc, CSharpIdentifierExpression target, CSharpCallExpression callee, CallType type)
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


    ExpressionSyntax GetValueSyntax(ValueStmtContext valueCtx)
    {
        if (valueCtx is VsICSContext vsics) {
            return GetCallInvocationExpression(vsics.implicitCallStmt_InStmt());
        }
        else {
            return ParseExpression(GetValue(valueCtx).ToString());
        }
    }


    ICSharpExpression GetValue(ValueStmtContext value)
    {
        using var _ = new TraceMethod(value);

        if (value is VsNewContext @new) {
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

    ArgumentListSyntax GetArgumentList(ArgsCallContext args) => ArgumentList(GetArgumentListCore(args));

    BracketedArgumentListSyntax GetBracketedArgumentList(ArgsCallContext args) => BracketedArgumentList(GetArgumentListCore(args));

    SeparatedSyntaxList<ArgumentSyntax> GetArgumentListCore(ArgsCallContext args)
    {
        if (args is null)
            return SeparatedList<ArgumentSyntax>();

        IEnumerable<ArgumentSyntax> GetArgsCore()
            => args.argCall().Select(arg => {
                if (arg.valueStmt() is VsAssignContext assign) {
                    var argName = GetExpressionCall(assign.implicitCallStmt_InStmt()).ToString(); // TODO: look at this
                    var value = ParseExpression(GetValue(assign.valueStmt()).ToString());
                    return Argument(value).WithNameColon(NameColon(IdentifierName(argName)));
                }
                else {
                    return Argument(GetValueSyntax(arg.valueStmt()));
                }
            });

        return SeparatedList<ArgumentSyntax>(GetArgsCore()
            .Select(a => (SyntaxNodeOrToken)a)
            .Intersperse(Token(SyntaxKind.CommaToken)));
    }


    static CSharpIdentifierExpression GetIdentifierReference(IIdentifierContext identifier, TypeHintContext typeHint)
    {
        if (identifier is null) {
            throw new ArgumentNullException(nameof(identifier));
        }

        using var _ = new TraceMethod(identifier);

        return new CSharpIdentifierExpression(GetIdentifier(identifier), GetType(typeHint));
    }

    static SyntaxToken GetIdentifierToken(IIdentifierContext identifier)
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

        return Identifier(text);
    }

    static IdentifierNameSyntax GetIdentifierNameToken(IIdentifierContext identifier)
    {
        return IdentifierName(GetIdentifierToken(identifier));
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

  
    static TypeSyntax GetTypeRoslyn(AsTypeClauseContext asType)
    {
        if (asType == null) {
            return PredefinedType(Token(SyntaxKind.VoidKeyword));
        }
        if (asType.fieldLength() is FieldLengthContext length) {
            throw NotSupported(length);
        }
        return GetTypeRoslyn(asType.type());
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


    static TypeSyntax GetTypeRoslyn(TypeContext type)
    {
        static PredefinedTypeSyntax Predefined(SyntaxKind kind) => PredefinedType(Token(kind));
        
        TypeSyntax ts;
        if (type.complexType() is ComplexTypeContext complex) {
            ts = ParseTypeName(complex.GetText());
        }
        else if (type.baseType() is BaseTypeContext baseType) {
            var typeSymbol = ((ITerminalNode)baseType.GetChild(0)).Symbol;
            ts = typeSymbol.Type switch {
                BOOLEAN => Predefined(SyntaxKind.BoolKeyword),
                BYTE => Predefined(SyntaxKind.ByteKeyword),
                COLLECTION => ParseTypeName("System.Collections.List<object>"),
                DATE => ParseTypeName("System.DateTime"),
                DOUBLE => Predefined(SyntaxKind.DoubleKeyword),
                INTEGER => Predefined(SyntaxKind.ShortKeyword),
                LONG => Predefined(SyntaxKind.IntKeyword),
                SINGLE => Predefined(SyntaxKind.FloatKeyword),
                STRING => Predefined(SyntaxKind.StringKeyword),
                OBJECT => Predefined(SyntaxKind.ObjectKeyword),
                VARIANT => Predefined(SyntaxKind.ObjectKeyword),
                _ => ParseTypeName(typeSymbol.Text)
            };
        }
        else {
            throw NotSupported(type);
        }

        if (type.LPAREN() != null || type.RPAREN() != null) {
            ts = ArrayType(ts);
        }

        return ts;
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

    static SyntaxKind GetVisibilityKeyword(IVisibilityContext v)
    {
        if (v is null) {
            return SyntaxKind.PrivateKeyword;
        }
        else if (v.PRIVATE() != null) {
            return SyntaxKind.PrivateKeyword;
        }
        else if (v.PUBLIC() != null || v.GLOBAL() != null) {
            return SyntaxKind.PublicKeyword;
        }
        else if (v.FRIEND() != null) {
            return SyntaxKind.InternalKeyword;
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
    static Exception NotSupported(object context, string message = null, [CallerMemberName] string caller = null)
    {
        if (string.IsNullOrEmpty(message)) {
            message = "Not supported";
        }
        if (context is ParserRuleContext parser) {
            throw new NotSupportedException($"{message} from {caller}: '{parser.GetText()}'\r\n{new ConsoleVisitor().Visit(parser)}");
        }
        else {
            throw new NotSupportedException($"{message} from {caller}: '{context.GetType().Name}'");
        }
    }

    class TraceMethod : IDisposable
    {
        public TraceMethod(object ctx, [CallerMemberName] string procedure = null)
        {
#if DEBUG
            Trace.TraceInformation($"{procedure}({ctx?.GetType().Name}) \"{Utils.EscapeWhitespace((ctx as ParserRuleContext)?.GetText() ?? string.Empty, false)}\"");
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
