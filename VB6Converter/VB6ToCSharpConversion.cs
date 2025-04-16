using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter;

public record class TransformError(string Message = null, object Context = null);

public class VB6ToCSharpConversion
{
    readonly Stack<ImplicitCallStmt_InStmtContext> _with = [];
    readonly List<ComReference> _comReferences = [];

    SyntaxToken? _label = null;

    public IReadOnlyList<ComReference> ComReferences => _comReferences;

    public string Name { get; }

    public List<TransformError> Errors { get; } = [];

    public CompilationUnitSyntax CompilationUnit { get; }

    public FileScopedNamespaceDeclarationSyntax Namespace { get; }

    public ClassDeclarationSyntax Class { get; }


    public VB6ToCSharpConversion(ModuleContext module, string name, string ns, bool isStatic)
    {
        using var _ = new TraceMethod(module);

        Name = name ?? throw new ArgumentNullException(nameof(name));

        CompilationUnit = CompilationUnit()
            .AddUsings(
                UsingDirective(ParseName("System")),
                UsingDirective(ParseName("System.Collections.Generic")));

        Class = GetClass(module, name, isStatic);

        Namespace = FileScopedNamespaceDeclaration(IdentifierName(ns ?? name))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(Class));

        CompilationUnit = CompilationUnit
            .WithMembers(SingletonList<MemberDeclarationSyntax>(Namespace));

        CompilationUnit = CompilationUnit.NormalizeWhitespace();
    }

    ClassDeclarationSyntax GetClass(ModuleContext module, string name, bool isStatic)
    {
        IEnumerable<SyntaxToken> GetModifiers()
        {
            yield return Token(SyntaxKind.PublicKeyword);
            if (isStatic) {
                yield return Token(SyntaxKind.StaticKeyword);
            }
            yield return Token(SyntaxKind.PartialKeyword);
        }

        var c = ClassDeclaration(name)
            .WithModifiers(TokenList(GetModifiers()));

        if (module.moduleBody() is ModuleBodyContext body) {
            foreach (var member in body.moduleBodyElement()) {
                try {
                    foreach (var decl in GetMemberDeclarations(member, isStatic)) {
                        if (decl is PropertyDeclarationSyntax property) {
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
                            c = c.AddMembers(decl);
                        }
                    }
                }
                catch (Exception ex) {
                    Errors.Add(new(ex.Message, member));
                }
            }
        }

        var rewriter = new VBFunctionRewriter();
        c = (ClassDeclarationSyntax)rewriter.Visit(c);
        return c;
    }

    IEnumerable<MemberDeclarationSyntax> GetMemberDeclarations(ModuleBodyElementContext e, bool isStatic)
    {
        using var _ = new TraceMethod(e);

        if (e.moduleBlock() is ModuleBlockContext moduleBlock) {
            if (moduleBlock.block() is BlockContext block) {
                foreach (var stmt in block.blockStmt()) {
                    if (stmt.constStmt() is ConstStmtContext @const) {
                        foreach (var f in GetConstantFields(@const)) {
                            yield return f;
                        }
                        
                    }
                    else if (stmt.variableStmt() is VariableStmtContext var) {
                        foreach (var f in GetVariableFields(var)) {
                            yield return f;
                        }
                    }
                    else if (stmt.attributeStmt() is AttributeStmtContext attr) {
                        // ignore
                    }
                    else {
                        throw NotSupported(stmt);
                    }
                }
            }
        }

        else if (e.enumerationStmt() is EnumerationStmtContext enumCtx) {
            yield return GetEnum(enumCtx);
        }
        else if (e.typeStmt() is TypeStmtContext typeCtx) {
            yield return GetStruct(typeCtx);
        }

        else if (e.subStmt() is SubStmtContext sub) {
            yield return GetMethod(sub);
        }
        else if (e.functionStmt() is FunctionStmtContext func) {
            yield return GetMethod(func);
        }
        else if (e.declareStmt() is DeclareStmtContext declare) {
            yield return GetExtern(declare);
        }

        else if (e.eventStmt() is EventStmtContext @event) {
            yield return GetEvent(@event);
        }

        else if (e.propertyAccessor() is IPropertyContext prop) {
            yield return GetProperty(prop);
        }

        else if (e.macroConstStmt() is MacroConstStmtContext macroConst) {
            //
        }

        else {
            throw NotSupported(e);
        }
        

        StructDeclarationSyntax GetStruct(TypeStmtContext type)
        {
            using var _ = new TraceMethod(type);

            return StructDeclaration(GetIdentifier(type.ambiguousIdentifier()))
                .WithModifiers(TokenList(GetVisibility(type.visibility())))
                .WithMembers(
                    List<MemberDeclarationSyntax>(
                        type.typeStmt_Element().Select(e =>
                            FieldDeclaration(
                                VariableDeclaration(GetType(e.asTypeClause()))
                                    .WithVariables(
                                        SingletonSeparatedList(
                                            VariableDeclarator(GetIdentifier(e.ambiguousIdentifier())))))
                            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                        )
                    )
                );

        }

        EnumDeclarationSyntax GetEnum(EnumerationStmtContext e)
        {
            using var _ = new TraceMethod(e);

            return EnumDeclaration(GetIdentifier(e.ambiguousIdentifier()))
                .WithModifiers(TokenList(GetVisibility(e.publicPrivateVisibility())))
                .WithMembers(
                    SeparatedList(
                        e.enumerationStmt_Constant().Select(c => {
                            var m = EnumMemberDeclaration(GetIdentifier(c.ambiguousIdentifier()));
                            if (c.valueStmt() is ValueStmtContext v) {
                                m = m.WithEqualsValue(EqualsValueClause(GetValue(v)));
                            }
                            return m;
                        }))
                    );
        }

        MethodDeclarationSyntax GetMethod(IMethodContext methodCtx)
        {
            using var _ = new TraceMethod(methodCtx);

            var type = GetType(methodCtx.asTypeClause(), assumeObjectIfMissing: methodCtx.IsFunction);
            var name = GetIdentifier(methodCtx.ambiguousIdentifier());
            var visb = GetVisibility(methodCtx.visibility());
            var body = GetBlock(methodCtx.block());

            // Rewrite return style

            var method = MethodDeclaration(type, name)
                .WithModifiers(TokenList(new SyntaxToken?[] {
                    visb,
                    isStatic ? Token(SyntaxKind.StaticKeyword) : null
                }.OfType<SyntaxToken>()))
                .WithParameterList(GetMethodParameters(methodCtx.argList()))
                .WithBody(body);

            var mr = new ReturnValueRewriter();
            method = (MethodDeclarationSyntax)mr.Visit(method);
            return method;
        }



        PropertyDeclarationSyntax GetProperty(IPropertyContext propCtx)
        {
            IEnumerable<SyntaxToken> GetModifiers()
            {
                yield return GetVisibility(propCtx.visibility());
                if (isStatic || propCtx.STATIC() is not null) {
                    yield return Token(SyntaxKind.StaticKeyword);
                }
            }

            var name = GetIdentifier(propCtx.ambiguousIdentifier());
            var body = GetBlock(propCtx.block());

            SyntaxKind kind;
            TypeSyntax type;

            if (propCtx is PropertyGetStmtContext get) {
                kind = SyntaxKind.GetAccessorDeclaration;
                type = GetType(get.asTypeClause());
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

            var prop = PropertyDeclaration(type, name)
                .WithModifiers(TokenList(GetModifiers()))
                .WithAccessorList(AccessorList(
                    SingletonList(AccessorDeclaration(kind)
                        .WithBody(body))));

            var mr = new ReturnValueRewriter();
            prop = (PropertyDeclarationSyntax)mr.Visit(prop);
            return prop;
        }

        MethodDeclarationSyntax GetExtern(DeclareStmtContext declare)
        {
            using var _ = new TraceMethod(declare);

            IEnumerable<AttributeArgumentSyntax> GetAttributeArguments()
            {
                string library = declare.STRINGLITERAL(0).GetText().Trim('"');
                yield return AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(library)));

                if (declare.STRINGLITERAL(1) is ITerminalNode aliasNode) {
                    string alias = aliasNode.GetText().Trim('"');
                    yield return AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(alias)))
                        .WithNameEquals(NameEquals("EntryPoint"));
                }
            }

            return MethodDeclaration(
                GetType(declare.asTypeClause()),
                GetIdentifier(declare.ambiguousIdentifier()))
                .WithModifiers(TokenList(
                    GetVisibility(declare.visibility()),
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
        
        EventDeclarationSyntax GetEvent(EventStmtContext eventCtx)
        {
            IEnumerable<SyntaxToken> GetModifiers()
            {
                yield return GetVisibility(eventCtx.visibility());
                if (isStatic) {
                    yield return Token(SyntaxKind.StaticKeyword);
                }
            }

            var id = GetIdentifierName(eventCtx.ambiguousIdentifier());
            var args = GetMethodParameters(eventCtx.argList());

            if (args.ChildNodes().Any()) {
                throw NotSupported(eventCtx);
            }

            return EventDeclaration(ParseTypeName("EventHandler"), id.Identifier)
                .WithModifiers(TokenList(GetModifiers()))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        }


        ParameterListSyntax GetMethodParameters(ArgListContext argsContext)
        {
            using var _ = new TraceMethod(argsContext);

            ParameterSyntax GetParameter(ArgContext arg)
            {
                var parameter = Parameter(GetIdentifier(arg.ambiguousIdentifier()))
                    .WithType(GetType(arg.asTypeClause()));

                if (arg.argDefaultValue() is ArgDefaultValueContext def) {
                    parameter = parameter.WithDefault(EqualsValueClause(GetValue(def.valueStmt())));
                }

                if (arg.PARAMARRAY() is not null) {
                    parameter = parameter
                        .WithModifiers(TokenList(Token(SyntaxKind.ParamsKeyword)))
                        .WithType(ArrayType(parameter.Type));
                    
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
                    GetVisibility(@const.publicPrivateGlobalVisibility()),
                    Token(SyntaxKind.ConstKeyword))));


        IEnumerable<SyntaxToken> GetModifiers(IVisibilityContext visibility)
        {
            yield return GetVisibility(visibility);
            if (isStatic) {
                yield return Token(SyntaxKind.StaticKeyword);
            }
        }

        IEnumerable<FieldDeclarationSyntax> GetVariableFields(VariableStmtContext var)
            => GetVariableDeclarations(var).Select(v => FieldDeclaration(v)
                .WithModifiers(TokenList(GetModifiers(var.visibility()))));
    }

    

    IEnumerable<VariableDeclarationSyntax> GetConstantDeclarations(ConstStmtContext @const)
    {
        using var _ = new TraceMethod(@const);
        
        foreach (var sub in @const.constSubStmt()) {
            var initializer = GetValue(sub.valueStmt());

            var type = GetType(sub.asTypeClause());
            if (type is PredefinedTypeSyntax pre && pre.Keyword.IsKind(SyntaxKind.VoidKeyword)) {
                if (initializer is LiteralExpressionSyntax literal) {
                    if (literal.IsKind(SyntaxKind.NumericLiteralExpression)) {
                        type = PredefinedType(Token(SyntaxKind.IntKeyword));
                    }
                    else if (literal.IsKind(SyntaxKind.StringLiteralExpression)) {
                        type = PredefinedType(Token(SyntaxKind.StringKeyword));
                    }
                }
            }

            yield return VariableDeclaration(type)
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(GetIdentifier(sub.ambiguousIdentifier()))
                            .WithInitializer(EqualsValueClause(initializer))
                    )
                );
        }
    }

    IEnumerable<VariableDeclarationSyntax> GetVariableDeclarations(VariableStmtContext var, bool defaultInit = false)
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

            var baseType = GetType(sub.asTypeClause());
            var type = isArray
                ? ArrayType(baseType, SingletonList(ArrayRankSpecifier(SeparatedList<ExpressionSyntax>(omittedExpressions))))
                : baseType;

            var variable = VariableDeclarator(name);
            if (arrayDimensions.Count > 0) {
                variable = variable.WithInitializer(EqualsValueClause(
                    ArrayCreationExpression(((ArrayTypeSyntax)type)
                        .WithRankSpecifiers(SingletonList(
                            ArrayRankSpecifier(SeparatedList(arrayDimensions.Select(i => ParseExpression(i)))
                        ))
                    ))
                ));
            }

            if (defaultInit) {
                variable = variable.WithInitializer(
                    EqualsValueClause(
                        LiteralExpression(SyntaxKind.DefaultLiteralExpression, Token(SyntaxKind.DefaultKeyword))));
            }

            yield return VariableDeclaration(type).WithVariables(SingletonSeparatedList(variable));
        }
    }


    // Statements

    StatementSyntax GetBlock(BlockContext block, bool allowCollapse) => GetBlock(block, allowCollapse, GetMethodStatements);

    BlockSyntax GetBlock(BlockContext block) => (BlockSyntax)GetBlock(block, false, GetMethodStatements);

    static StatementSyntax GetBlock(BlockContext block, bool allowCollapse, Func<BlockStmtContext, IEnumerable<StatementSyntax>> statementFactory)
        => RoslynHelpers.GetBlock(block?.blockStmt().SelectMany(statementFactory).ToArray() ?? [], allowCollapse);

    
    IEnumerable<StatementSyntax> GetMethodStatements(BlockStmtContext stmt)
    {
        using var _ = new TraceMethod(stmt);

        IEnumerable<StatementSyntax> GetStatements()
        {
            if (stmt.constStmt() is ConstStmtContext @const) {
                foreach (var c in GetConstantDeclarations(@const)) {
                    yield return LocalDeclarationStatement(c);
                }
            }
            else if (stmt.variableStmt() is VariableStmtContext var) {
                foreach (var v in GetVariableDeclarations(var, true)) {
                    yield return LocalDeclarationStatement(v);
                }
            }
            else if (stmt.redimStmt() is RedimStmtContext redim) {
                yield return GetRedim(redim);
            }

            else if (stmt.call() is ICallContext call) {
                yield return GetCallStatement(call);
            }
            else if (stmt.assignment() is IAssignmentContext assignment) {
                yield return ExpressionStatement(GetAssignment(assignment));
            }
            else if (stmt.withStmt() is WithStmtContext with) {
                yield return GetWith(with);
            }

            else if (stmt.ifThenElseStmt() is IfThenElseStmtContext ifthen) {
                yield return GetIf(ifthen);
            }
            else if (stmt.selectCaseStmt() is SelectCaseStmtContext select) {
                yield return GetSelectCase(select);
            }

            else if (stmt.forNextStmt() is ForNextStmtContext fornext) {
                yield return GetForNext(fornext);
            }
            else if (stmt.forEachStmt() is ForEachStmtContext forEach) {
                yield return GetForEach(forEach);
            }
            else if (stmt.doLoopStmt() is DoLoopStmtContext doLoop) {
                yield return GetDoLoop(doLoop);
            }
            else if (stmt.whileWendStmt() is WhileWendStmtContext whileWend) {
                yield return GetWhile(whileWend);
            }

            else if (stmt.loadStmt() is LoadStmtContext load) {
                yield return EmptyStatement().WithTrailingTrivia(Comment($"// {load.GetText()}"));
            }
            else if (stmt.unloadStmt() is UnloadStmtContext unload) {
                yield return EmptyStatement().WithTrailingTrivia(Comment($"// {unload.GetText()}"));
            }

            else if (stmt.openStmt() is OpenStmtContext open) {
                yield return GetOpen(open);
            }
            else if (stmt.printStmt() is PrintStmtContext print) {
                yield return GetPrint(print);
            }
            else if (stmt.closeStmt() is CloseStmtContext close) {
                yield return GetClose(close);
            }
            else if (stmt.eraseStmt() is EraseStmtContext erase) {
                foreach (var e in GetErase(erase)) {
                    yield return e;
                }
            }

            else if (stmt.raiseEventStmt() is RaiseEventStmtContext raise) {
                var name = GetIdentifierName(raise.ambiguousIdentifier());
                var args = raise.argsCall();
                if (args != null && args.ChildCount > 0) {
                    throw NotSupported(raise);
                }

                yield return ParseStatement($"{name}?.Invoke(this, EventArgs.Empty);");
            }

            else if (stmt.goToStmt() is GoToStmtContext goTo) {
                yield return GetGoTo(goTo);
            }
            else if (stmt.resumeStmt() is ResumeStmtContext resume) {
                yield return GetResume(resume);
            }
            else if (stmt.onErrorStmt() is OnErrorStmtContext onerror) {
                yield return EmptyStatement().WithTrailingTrivia(TriviaList(Comment($"// {onerror.GetText()}")));
            }

            else if (stmt.exitStmt() is ExitStmtContext exit) {
                yield return GetExit(exit);
            }
            else if (stmt.endStmt() is EndStmtContext end) {
                yield return GetEnd(end);
            }

            else if (stmt.attributeStmt() is AttributeStmtContext attribute) {
                // do nothing
            }

            else if (stmt.beepStmt() is BeepStmtContext beepCtx) {
                yield return ParseStatement("Console.Beep();");
            }
            else if (stmt.sendkeysStmt() is SendkeysStmtContext sendKeys) {
                var sendExpr = ParseExpression("SendKeys.Send");
                foreach (var send in sendKeys.valueStmt()) {
                    var value = GetValue(send);
                    yield return ExpressionStatement(
                        InvocationExpression(sendExpr, ArgumentList(value))
                    );
                }
            }
            else {
                throw NotSupported(stmt);
            }
        }

        IEnumerable<StatementSyntax> GetStatementsWithLabels()
        {
            if (stmt.lineLabel() is LineLabelContext label) {
                _label = GetIdentifier(label.ambiguousIdentifier());
            }
            else {
                foreach (var s in GetStatements()) {
                    if (_label is SyntaxToken l) {
                        yield return LabeledStatement(l, s);
                        _label = null;
                    }
                    else {
                        yield return s;
                    }
                }
            }
        }

        try {
            return [.. GetStatementsWithLabels()];
        }
        catch (Exception ex) {
            Errors.Add(new(ex.Message, stmt));
            return [
                ParseStatement(stmt.GetText()).WithTrailingTrivia(TriviaList(Comment($"// ERROR: {ex.Message.Replace(Environment.NewLine, Environment.NewLine + "// ")}{Environment.NewLine}")))
            ];
        }
    }


    AssignmentExpressionSyntax GetAssignment(IAssignmentContext assignment)
    {
        using var _ = new TraceMethod(assignment);
        var identifier = GetCallIdentifierExpression(assignment.implicitCallStmt_InStmt(), true);
        var value = GetValue(assignment.valueStmt());
        return AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, identifier, value);
    }


    StatementSyntax GetWith(WithStmtContext with)
    {
        using var _ = new TraceMethod(with);

        _with.Push(with.implicitCallStmt_InStmt());
        try {
            var block = GetBlock(with.block());

            if (block.Statements.Count == 0) {
                return EmptyStatement();
            }
            else if (block.Statements.Count == 1) {
                return block.Statements[0];
            }
            else {
                return GetBlock(with.block());
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

        var invocation = call.segments().LastOrDefault();
        var args = invocation?.args().FirstOrDefault();

        var identifier = GetCallIdentifierExpression(call);
        
        if (invocation is ICS_S_ProcedureOrArrayCallContext
            || invocation is ECS_ProcedureCallContext
            || invocation is ECS_MemberProcedureCallContext
            || invocation is ICS_B_ProcedureCallContext
            || invocation is ICS_B_MemberProcedureCallContext
            || args?.ChildCount > 0) {

            var argList = GetArgumentList(args);
            return InvocationExpression(identifier).WithArgumentList(argList);
        }
        else {
            return identifier;
        }
    }

    ExpressionSyntax GetCallIdentifierExpression(ICallContext call, bool preferArray = false)
    {
        using var _ = new TraceMethod(call);

        var segments = call.segments().ToArray();
        if (call.IsPartial && _with.TryPeek(out var with)) {
            segments = with.segments().Concat(segments).ToArray();
        }

        ExpressionSyntax expr = null;

        var invocation = segments.LastOrDefault();
        foreach (var s in segments) {
            var id = GetIdentifierName(s);

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

    IdentifierNameSyntax GetIdentifierName(ICallTargetContext target)
        => GetIdentifierName(target.identifier(), target.typeHint()); // todo: handle cast


    IfStatementSyntax GetIf(IfThenElseStmtContext ifthen)
    {
        using var _ = new TraceMethod(ifthen);

        IfStatementSyntax root = null;
        IfStatementSyntax current = null;

        if (ifthen is BlockIfThenElseContext @if) {
            if (@if.ifBlockStmt() is IfBlockStmtContext ifBlock) {
                var condition = GetValue(ifBlock.ifConditionStmt().valueStmt());
                var then = GetBlock(ifBlock.block());

                root = IfStatement(condition, then);
                current = root;
            }

            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var condition = GetValue(elseif.ifConditionStmt().valueStmt());
                    var then = GetBlock(elseif.block());

                    var next = IfStatement(condition, then);
                    current = current.WithElse(ElseClause(next));
                    current = next;
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                var block = GetBlock(@else.block());
                current = current.WithElse(ElseClause(block));
            }
        }
        else if (ifthen is InlineIfThenElseContext inline && inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
            var condition = GetValue(ifcond.valueStmt());
            var block = GetBlock(inline.block(0), true);
           
            root = IfStatement(condition, block);

            if (inline.block(1) is BlockContext elseBlock) {
                var elseStatement = GetBlock(elseBlock, true);
                root = root.WithElse(ElseClause(elseStatement));
            }
        }
        else {
            NotSupported(ifthen);
        }

        return root;
    }

    StatementSyntax GetSelectCase(SelectCaseStmtContext select)
    {
        var condition = GetValue(select.valueStmt());

        IEnumerable<SwitchSectionSyntax> GetSections()
        {
            foreach (var caseStmt in select.sC_Case()) {
                var block = GetBlock(caseStmt.block()).AddStatements(BreakStatement());

                if (caseStmt.sC_Cond() is CaseCondExprContext expr) {
                    var labels = List(GetLabels(expr).ToArray());
                    yield return SwitchSection(labels, block.Statements);
                }
                else if (caseStmt.sC_Cond() is CaseCondElseContext @else) {
                    var labels = SingletonList<SwitchLabelSyntax>(DefaultSwitchLabel());
                    yield return SwitchSection(labels, block.Statements);
                }
                else {
                    NotSupported(caseStmt);
                }
            }
        };

        IEnumerable<SwitchLabelSyntax> GetLabels(CaseCondExprContext cond)
        {
            foreach (var c in cond.sC_CondExpr()) {
                if (c is CaseCondExprValueContext valueCond) {
                    var value = GetValue(valueCond.valueStmt());
                    yield return CaseSwitchLabel(value);
                }
                else if (c is CaseCondExprIsContext isCond) {
                    throw NotSupported(isCond);
                }
                else if (c is CaseCondExprToContext toCond) {
                    throw NotSupported(toCond);
                }
                else {
                    throw NotSupported(c);
                }
            }
        }

        try {
            return SwitchStatement(condition, List(GetSections()));
        }
        catch (NotSupportedException) {
            return SwitchAsIf(select);
        }
    }

    private StatementSyntax SwitchAsIf(SelectCaseStmtContext select)
    {
        var condition = GetValue(select.valueStmt());

        List<IfStatementSyntax> clauses = [];
        ElseClauseSyntax @else = null;

        foreach (var caseStmt in select.sC_Case()) {
            var block = GetBlock(caseStmt.block());

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
                NotSupported(caseStmt);
            }
        }

        ExpressionSyntax GetCondition(ExpressionSyntax condition, SC_CondExprContext c)
        {
            switch (c) {
                case CaseCondExprValueContext valueCond: 
                    var value = GetValue(valueCond.valueStmt());
                    return BinaryExpression(SyntaxKind.EqualsExpression, condition, value);
                    

                case CaseCondExprToContext toCond: 
                    var min = GetValue(toCond.valueStmt(0));
                    var max = GetValue(toCond.valueStmt(1));

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
                        IS => throw NotSupported(isCond),
                        LIKE => throw NotSupported(isCond),
                        _ => throw NotSupported(isCond)
                    };

                    var v = GetValue(isCond.valueStmt());
                    return BinaryExpression(kind, condition, v);

                default:
                    throw NotSupported(c);
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

    ForStatementSyntax GetForNext(ForNextStmtContext forNext)
    {
        var type = forNext.asTypeClause() is AsTypeClauseContext asType
            ? GetType(asType)
            : PredefinedType(Token(SyntaxKind.IntKeyword));

        var variable = GetIdentifierName(forNext.iCS_S_VariableOrProcedureCall());

        var start = GetValue(forNext.valueStmt(0));
        var end = GetValue(forNext.valueStmt(1));
        var step = GetValue(forNext.valueStmt(2));

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

        var block = GetBlock(forNext.block(), false, stmt => {
            if (stmt.exitStmt() is ExitStmtContext exit && exit.EXIT_FOR() is not null) {
                return [ BreakStatement() ];
            }
            else {
                return GetMethodStatements(stmt);
            }
        });

        return ForStatement(block)
            .WithInitializers(SingletonSeparatedList<ExpressionSyntax>(decl))
            .WithCondition(BinaryExpression(ops.Item3, variable, GetValue(forNext.valueStmt(1))))
            .WithIncrementors(SingletonSeparatedList(incrementor));
    }

    StatementSyntax GetForEach(ForEachStmtContext forEach)
    {
        using var _ = new TraceMethod(forEach);

        var variable = GetIdentifier(forEach.ambiguousIdentifier(0));
        var enumerator = GetValue(forEach.valueStmt());
        var statements = GetBlock(forEach.block(), false, stmt => {
            if (stmt.exitStmt() is ExitStmtContext exit && exit.EXIT_FOR() is not null) {
                return [BreakStatement()];
            }
            else {
                return GetMethodStatements(stmt);
            }
        });

        return ForEachStatement(
            IdentifierName(Identifier(TriviaList(), SyntaxKind.VarKeyword, "var", "var", TriviaList())),
            variable,
            enumerator,
            statements);
    }

    StatementSyntax GetDoLoop(DoLoopStmtContext doLoop)
    {
        var condition = GetValue(doLoop.valueStmt());
        var statements = GetBlock(doLoop.block());
        return WhileStatement(condition, statements);
    }

    StatementSyntax GetWhile(WhileWendStmtContext whileWend)
    {
        var condition = GetValue(whileWend.valueStmt());
        var statements = GetBlock(whileWend.block().First());
        return WhileStatement(condition, statements);
    }


    StatementSyntax GetRedim(RedimStmtContext redim)
    {
        using var _ = new TraceMethod(redim);

        var statements = redim.redimSubStmt().Select(rd => {
            var variable = GetCallIdentifierExpression(rd.implicitCallStmt_InStmt());

            var type = GetType(rd.asTypeClause())
                ?? throw NotSupported(redim, "Inferred array type not supported.");

            var subscripts = rd.subscripts().subscript().Select(s => GetValue(s.valueStmt(0)));

            var arrayType = ArrayType(type, SingletonList(ArrayRankSpecifier(SeparatedList(subscripts))));

            if (redim.PRESERVE() is not null) {
                throw NotSupported(redim, "Preserve not supported");
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


    StatementSyntax GetOpen(OpenStmtContext open)
    {
        var file = GetValue(open.valueStmt(0));
        var name = GetValue(open.valueStmt(1));

        var variable = name switch {
            IdentifierNameSyntax n => n,
            LiteralExpressionSyntax l => IdentifierName(l.Token.Text),
            _ => throw NotSupported(open)
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
            )));
    }

    StatementSyntax GetPrint(PrintStmtContext print)
    {
        using var _ = new TraceMethod(print);

        var target = GetValue(print.valueStmt());

        var outputs = print.outputList().outputList_Expression().Select(o => {
            if (o.SPC() is ITerminalNode) {
                throw NotSupported(o);
            }
            if (o.TAB() is ITerminalNode) {
                throw NotSupported(o);
            }

            if (o.valueStmt() is ValueStmtContext value) {
                return GetValue(value);
            }
            else {
                throw NotSupported(o);
            }
        }).ToArray();

        var statements = outputs.Select(o => ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, target, IdentifierName("Write")))
                    .WithArgumentList(RoslynHelpers.ArgumentList(outputs))))
            .ToArray();

        return RoslynHelpers.GetBlock(statements, true);
    }

    IEnumerable<StatementSyntax> GetErase(EraseStmtContext erase)
    {
        foreach (var v in erase.valueStmt()) {
            var value = GetValue(v);

            yield return ExpressionStatement(
                InvocationExpression(
                    ParseExpression("File.Delete"),
                    ArgumentList(value)));
        }
    }

    StatementSyntax GetClose(CloseStmtContext close)
    {
        var value = GetValue(close.valueStmt(0));
        return ExpressionStatement(
            InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, 
                    value, IdentifierName("Dispose")),
                ArgumentList()))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }


    StatementSyntax GetGoTo(GoToStmtContext goTo)
    {
        using var _ = new TraceMethod(goTo);
        var label = GetValue(goTo.valueStmt());
        return GotoStatement(SyntaxKind.GotoStatement, label);
    }

    StatementSyntax GetResume(ResumeStmtContext resume)
    {
        if (resume.ambiguousIdentifier() is AmbiguousIdentifierContext identifier) {
            var expr = GetIdentifierName(identifier);
            return GotoStatement(SyntaxKind.GotoStatement, expr);
        }
        else {
            return EmptyStatement().WithTrailingTrivia(TriviaList(Comment(resume.GetText())));
        }
    }

    StatementSyntax GetExit(ExitStmtContext exit)
    {
        if (exit.EXIT_SUB() is not null || exit.EXIT_FUNCTION() is not null || exit.EXIT_PROPERTY() is not null) {
            return ReturnStatement(); // will be fixed up later
        }
        else if (exit.EXIT_DO() is not null || exit.EXIT_FOR() is not null) {
            return BreakStatement();
        }
        else {
            throw NotSupported(exit);
        }
    }

    StatementSyntax GetEnd(EndStmtContext end)
    {
        return ExpressionStatement(
            ParseExpression("Application.Exit()")
        );
    }


    ExpressionSyntax GetValue(ValueStmtContext valueCtx)
    {
        using var _ = new TraceMethod(valueCtx);

        try {
            if (valueCtx is null) {
                return LiteralExpression(SyntaxKind.NullLiteralExpression);
            }
            else if (valueCtx is VsICSContext vsics) {
                return GetCallInvocationExpression(vsics.implicitCallStmt_InStmt());
            }
            else if (valueCtx is VsLiteralContext literal) {
                return GetLiteral(literal.literal());
            }
            else if (valueCtx is VsNewContext @new) {
                return ObjectCreationExpression(IdentifierName(@new.valueStmt().GetText()))
                    .WithArgumentList(ArgumentList());
            }
            else if (valueCtx is VsStructContext @struct) {
                if (@struct.valueStmt().Length > 1) {
                    throw NotSupported("Struct with more than one value.");
                }

                var value = GetValue(@struct.valueStmt(0));
                return ParenthesizedExpression(value);
            }
            else if (valueCtx is IOperatorContext oper) {
                return GetOperator(oper);
            }
            else {
                throw NotSupported($"Unknown value: {valueCtx.GetType().Name} {valueCtx.GetText()}");
            }
        }
        catch (Exception ex) {
            Errors.Add(new(ex.Message, valueCtx));
            return ParseExpression(valueCtx.GetText());
        }
    }

    ExpressionSyntax GetOperator(IOperatorContext oper)
    {
        var values = oper.valueStmt().Select(GetValue).ToArray();

        if (oper is VsPowContext pow) {
            return InvocationExpression(ParseName("Math.Pow"), ArgumentList(values));
        }
        
        SyntaxKind kind = oper switch {
            VsAmpContext => SyntaxKind.AddExpression, // string concat

            VsAddContext   => SyntaxKind.AddExpression,
            VsMinusContext => SyntaxKind.SubtractExpression,
            VsMultContext  => SyntaxKind.MultiplyExpression,
            VsDivContext   => SyntaxKind.DivideExpression,
            VsModContext   => SyntaxKind.ModuloExpression,
            
            VsNegationContext => SyntaxKind.UnaryMinusExpression,

            VsEqContext  => SyntaxKind.EqualsExpression,
            VsNeqContext => SyntaxKind.NotEqualsExpression,           
            VsGtContext  => SyntaxKind.GreaterThanExpression,
            VsGeqContext => SyntaxKind.GreaterThanOrEqualExpression,
            VsLtContext  => SyntaxKind.LessThanExpression,
            VsLeqContext => SyntaxKind.LessThanOrEqualExpression,
            
            VsAndContext => SyntaxKind.LogicalAndExpression,
            VsOrContext  => SyntaxKind.LogicalOrExpression,
            VsNotContext => SyntaxKind.LogicalNotExpression,
            VsXorContext => SyntaxKind.ExclusiveOrExpression,

            VsIsContext => SyntaxKind.IsExpression,

            _ => throw NotSupported(oper)
        };

        if (values.Length == 1) {
            return PrefixUnaryExpression(kind, values[0]);
        }
        else if (values.Length == 2) {
            return BinaryExpression(kind, values[0], values[1]);
        }
        else {
            throw NotSupported(oper);
        }
    }

    static ExpressionSyntax GetLiteral(LiteralContext lit)
    {
        if (lit.TRUE() is not null) {
            return LiteralExpression(SyntaxKind.TrueLiteralExpression);
        }
        else if (lit.FALSE() is not null) {
            return LiteralExpression(SyntaxKind.FalseLiteralExpression);
        }
        else if (lit.NOTHING() is not null || lit.NULL() is not null) {
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (lit.INTEGERLITERAL() is ITerminalNode @short) {
            
            var text = @short.Symbol.Text;
            text = text.TrimEnd('&');

            if (text.StartsWith('&')) {
                string hex = text[1..];
                int value = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal("0x" + hex, value));
            }
            else {
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt32(text)));
            }
        }
        else if (lit.DOUBLELITERAL() is ITerminalNode @double) {
            return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToDouble(@double.GetText().TrimEnd(['&']))));
        }
        else if (lit.STRINGLITERAL() is ITerminalNode @string) {
            string str = @string.Symbol.Text;
            str = str.Trim('"');
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(str));
        }
        else if (lit.DATELITERAL() is ITerminalNode @date) {
            var text = date.Symbol.Text.Trim('#');
            return InvocationExpression(
                ParseExpression("DateTime.Parse"),
                ArgumentList(
                    LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text))
                ));
        }
        else if (lit.FILENUMBER() is ITerminalNode file) {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(file.GetText().TrimStart('#')));
        }
        else if (lit.COLORLITERAL() is ITerminalNode color) {

            var text = color.Symbol.Text;
            text = text.TrimEnd('&');

            if (text.StartsWith('&')) {
                string hex = text.TrimStart(['&', 'H']);
                long value = long.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal("0x" + hex, value));
            }
            else {
                return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt64(text)));
            }

            //var hex = "0x" + color.Symbol.Text[2..].TrimEnd(['&']).PadLeft(6, '0').PadLeft(8, 'F');
            //var col = System.Drawing.Color.FromArgb(Convert.ToInt32(hex, 16));

            //if (col.IsNamedColor) {
            //    return InvocationExpression(
            //        MemberAccessExpression(
            //            SyntaxKind.SimpleMemberAccessExpression,
            //            IdentifierName("Color"), IdentifierName("FromName")
            //        ))
            //        .WithArgumentList(RoslynHelpers.ArgumentList(
            //            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(col.Name))
            //        ));
            //}
            //else if (col.A == 255) {
            //    return InvocationExpression(
            //        MemberAccessExpression(
            //            SyntaxKind.SimpleMemberAccessExpression,
            //            IdentifierName("Color"), IdentifierName("FromArgb")
            //        ))
            //        .WithArgumentList(RoslynHelpers.ArgumentList(
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.R)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.G)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.B))
            //        ));
            //}
            //else {
            //    return InvocationExpression(
            //        MemberAccessExpression(
            //            SyntaxKind.SimpleMemberAccessExpression,
            //            IdentifierName("Color"), IdentifierName("FromArgb")
            //        ))
            //        .WithArgumentList(RoslynHelpers.ArgumentList(
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.A)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.R)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.G)),
            //            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.B))
            //        ));
            //}
        }
        else {
            throw NotSupported(lit);
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
                    var argName = GetCallIdentifierExpression(assign.implicitCallStmt_InStmt());
                    var value = GetValue(assign.valueStmt());
                    return Argument(value).WithNameColon(NameColon((IdentifierNameSyntax)argName));
                }
                else {
                    return Argument(GetValue(arg.valueStmt()));
                }
            });

        return SeparatedList<ArgumentSyntax>(GetArgsCore()
            .Select(a => (SyntaxNodeOrToken)a)
            .Intersperse(Token(SyntaxKind.CommaToken)));
    }


    static IdentifierNameSyntax GetIdentifierName(IIdentifierContext identifier, TypeHintContext typeHint = null)
    {
        return IdentifierName(GetIdentifier(identifier));
    }

    static SyntaxToken GetIdentifier(IIdentifierContext identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

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


    static TypeSyntax GetType(AsTypeClauseContext asType, bool assumeObjectIfMissing = false)
    {
        if (asType == null) {
            return assumeObjectIfMissing 
                ? PredefinedType(Token(SyntaxKind.ObjectKeyword))
                : PredefinedType(Token(SyntaxKind.VoidKeyword));
        }
        if (asType.fieldLength() is FieldLengthContext length) {
            throw NotSupported(length);
        }
        return GetType(asType.type());
    }

    static TypeSyntax GetType(TypeContext type)
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
                COLLECTION => ParseTypeName("List<object>"),
                DATE => ParseTypeName("DateTime"),
                DOUBLE => Predefined(SyntaxKind.DoubleKeyword),
                INTEGER => Predefined(SyntaxKind.IntKeyword),
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


    static SyntaxToken GetVisibility(IVisibilityContext v)
    {
        SyntaxKind GetVisibilityKeyword()
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

        return Token(GetVisibilityKeyword());
    }

    


    [DebuggerHidden]
    static Exception NotSupported(object context, string message = null, [CallerMemberName] string caller = null)
    {
        if (string.IsNullOrEmpty(message)) {
            message = "Not supported";
        }

        if (context is ParserRuleContext parser) {
            message = $"{message} from {caller}: '{parser.GetText()}'\r\n{new ConsoleVisitor().Visit(parser)}";
        }
        else {
            message = $"{message} from {caller}: '{context.GetType().Name}'";
        }

        throw new NotSupportedException(message);
    }

    class TraceMethod : IDisposable
    {
        public TraceMethod(object ctx, [CallerMemberName] string procedure = null)
        {
#if DEBUG
            //Trace.TraceInformation($"{procedure}({ctx?.GetType().Name}) \"{Utils.EscapeWhitespace((ctx as ParserRuleContext)?.GetText() ?? string.Empty, false)}\"");
            //Trace.Indent();
#endif
        }

        public void Dispose()
        {
#if DEBUG
            //Trace.Unindent();
#endif
        }
    }
}
