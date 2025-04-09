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
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.CSharpModel;

public class CSharpTransformation(bool failOnError = true)
{
    readonly CSharpLegacyTransformation _legacy = new(failOnError);
    readonly Stack<ImplicitCallStmt_InStmtContext> _with = [];
    readonly List<ComReference> _comReferences = [];

    readonly List<IMethodRewriter> _rewriters = [
        new MsgBoxRewriter()
        ];

    public IReadOnlyList<ComReference> ComReferences => _comReferences;


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
                                VariableDeclaration(GetType(e.asTypeClause()))
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
                                m = m.WithEqualsValue(EqualsValueClause(GetValue(v)));
                            }
                            return m;
                        }))
                    );
        }

        MethodDeclarationSyntax GetMethod(IMethodContext methodCtx)
        {
            using var _ = new TraceMethod(methodCtx);

            var type = GetType(methodCtx.asTypeClause());
            var name = GetIdentifier(methodCtx.ambiguousIdentifier());
            var visb = GetVisibilityKeyword(methodCtx.visibility());
            var body = GetBlockSyntax(methodCtx.block());

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
            var body = GetBlockSyntax(propCtx.block());

            SyntaxKind kind;
            TypeSyntax type;

            if (propCtx is PropertyGetStmtContext get) {
                kind = SyntaxKind.GetAccessorDeclaration;
                type = GetType(get.asTypeClause());
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
                GetType(declare.asTypeClause()),
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
                    .WithType(GetType(arg.asTypeClause()));

                if (arg.argDefaultValue() is ArgDefaultValueContext def) {
                    parameter = parameter.WithDefault(EqualsValueClause(GetValue(def.valueStmt())));
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

        return @const.constSubStmt().Select(sub => VariableDeclaration(GetType(sub.asTypeClause()))
            .WithVariables(
                SingletonSeparatedList(
                    VariableDeclarator(Identifier(GetIdentifier(sub.ambiguousIdentifier())))
                        .WithInitializer(EqualsValueClause(GetValue(sub.valueStmt())))
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

            var baseType = GetType(sub.asTypeClause());
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

    BlockSyntax GetBlockSyntax(BlockContext block) => (BlockSyntax)GetBlockSyntax(block, false, GetMethodStatement);

    StatementSyntax GetBlockSyntax(BlockContext block, bool allowCollapse, Func<BlockStmtContext, StatementSyntax> statementFactory)
        => GetBlockSyntax(block?.blockStmt().Select(statementFactory).ToArray() ?? [], allowCollapse);

    StatementSyntax GetBlockSyntax(StatementSyntax[] statements, bool allowCollapse)
    {
        if (allowCollapse) {
            if (statements is null || statements.Length == 0) {
                return EmptyStatement();
            }
            else if (statements.Length == 1) {
                return statements[0];
            }
            else {
                return Block(statements);
            }
        }
        else {
            return Block(statements ?? []);
        }
    }


    StatementSyntax GetMethodStatement(BlockStmtContext stmt)
    {
        using var _ = new TraceMethod(stmt);

        if (stmt.redimStmt() is RedimStmtContext redim) {
            return GetRedim(redim);
        }
        else if (stmt.call() is ICallContext call) {
            return GetCallStatement(call);
        }
        else if (stmt.assignment() is IAssignmentContext assignment) {
            return ExpressionStatement(GetAssignment(assignment));
        }
        else if (stmt.withStmt() is WithStmtContext with) {
            return GetWith(with);
        }
        else if (stmt.ifThenElseStmt() is IfThenElseStmtContext ifthen) {
            return GetIf(ifthen);
        }
        else if (stmt.forNextStmt() is ForNextStmtContext fornext) {
            return GetForNext(fornext);
        }

        else if (stmt.printStmt() is PrintStmtContext print) {
            return GetPrint(print);
        }

        else {
            var legacy = _legacy.GetBlockStatements(stmt).Select(s => ParseStatement(s.ToString())).ToArray();
            if (legacy.Length == 0) {
                return EmptyStatement();
            }
            else if (legacy.Length == 1) {
                return legacy[0];
            }
            else {
                return Block(legacy);
            }
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
            var block = GetBlockSyntax(with.block());

            if (block.Statements.Count == 0) {
                return EmptyStatement();
            }
            else if (block.Statements.Count == 1) {
                return block.Statements[0];
            }
            else {
                return GetBlockSyntax(with.block());
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

        IMethodRewriter rewriter = null;

        var identifier = GetCallIdentifierExpression(call);
        foreach (var r in _rewriters) {
            if (r.RewriteIdentifier(identifier) is ExpressionSyntax rewritten) {
                identifier = rewritten;
                rewriter = r;
            }
        }

        if (invocation is ICS_S_ProcedureOrArrayCallContext 
            || invocation is ECS_ProcedureCallContext
            || invocation is ECS_MemberProcedureCallContext
            || invocation is ICS_B_ProcedureCallContext 
            || invocation is ICS_B_MemberProcedureCallContext 
            || args?.ChildCount > 0) {

            var argList = GetArgumentList(args);

            if (rewriter is not null) {
                argList = rewriter.RewriteArguments(argList, identifier);
            }

            return InvocationExpression(identifier).WithArgumentList(argList);
        }
        else {
            return identifier;
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
        => GetIdentifierNameToken(target.identifier()); // todo: handle cast


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

        return GetBlockSyntax(statements, true);
    }


    IfStatementSyntax GetIf(IfThenElseStmtContext ifthen)
    {
        using var _ = new TraceMethod(ifthen);

        IfStatementSyntax root = null;
        IfStatementSyntax current = null;

        if (ifthen is BlockIfThenElseContext @if) {
            if (@if.ifBlockStmt() is IfBlockStmtContext ifBlock) {
                var condition = GetValue(ifBlock.ifConditionStmt().valueStmt());
                var then = GetBlockSyntax(ifBlock.block());

                root = IfStatement(condition, then);
                current = root;
            }

            if (@if.ifElseIfBlockStmt() is IfElseIfBlockStmtContext[] elseifs) {
                foreach (var elseif in elseifs) {
                    var condition = GetValue(elseif.ifConditionStmt().valueStmt());
                    var then = GetBlockSyntax(elseif.block());

                    var next = IfStatement(condition, then);
                    current = current.WithElse(ElseClause(next));
                    current = next;
                }
            }

            if (@if.ifElseBlockStmt() is IfElseBlockStmtContext @else) {
                var block = GetBlockSyntax(@else.block());
                current = current.WithElse(ElseClause(block));
            }
        }
        else if (ifthen is InlineIfThenElseContext inline && inline.ifConditionStmt() is IfConditionStmtContext ifcond) {
            var condition = GetValue(ifcond.valueStmt());
            var statement = GetMethodStatement(inline.blockStmt(0));

            root = IfStatement(condition, statement);

            if (inline.blockStmt(1) is BlockStmtContext elseBlock) {
                var elseStatement = GetMethodStatement(elseBlock);
                root = root.WithElse(ElseClause(elseStatement));
            }
        }
        else {
            NotSupported(ifthen);
        }

        return root;
    }

    ForStatementSyntax GetForNext(ForNextStmtContext forNext)
    {
        var type = forNext.asTypeClause() is AsTypeClauseContext asType
            ? GetType(asType)
            : PredefinedType(Token(SyntaxKind.IntKeyword));

        var id = GetIdentifierName(forNext.iCS_S_VariableOrProcedureCall());

        var start = GetValue(forNext.valueStmt(0));
        var end   = GetValue(forNext.valueStmt(1));
        var step  = GetValue(forNext.valueStmt(2));

        bool stepIsOne = true, stepIsNegative = false;
        if (step is LiteralExpressionSyntax literal && literal.Token.Value is int istep) {
            stepIsOne = istep == 1 || istep == -1;
            stepIsNegative = istep < 0;
            if (stepIsNegative) {
                step = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(-istep));
            }
        }

        var decl = VariableDeclaration(type)
            .WithVariables(SingletonSeparatedList(
                VariableDeclarator(id.Identifier).WithInitializer(EqualsValueClause(start))));

        var ops = stepIsNegative
            ? (SyntaxKind.PostDecrementExpression, SyntaxKind.SubtractAssignmentExpression, SyntaxKind.GreaterThanOrEqualExpression)
            : (SyntaxKind.PostIncrementExpression, SyntaxKind.AddAssignmentExpression, SyntaxKind.LessThanOrEqualExpression);

        

        ExpressionSyntax incrementor = stepIsOne
            ? PostfixUnaryExpression(ops.Item1, id) 
            : AssignmentExpression(ops.Item2, id, step);

        var block = GetBlockSyntax(forNext.block(), true, stmt => {
            if (stmt.exitStmt() is ExitStmtContext exit && exit.EXIT_FOR() is not null) {
                return BreakStatement();
            }
            else {
                return GetMethodStatement(stmt);
            }
        });

        return ForStatement(block)
            .WithDeclaration(decl)
            .WithCondition(BinaryExpression(ops.Item3, id, GetValue(forNext.valueStmt(1))))
            .WithIncrementors(SingletonSeparatedList(incrementor));
    }
    

    ExpressionSyntax GetValue(ValueStmtContext valueCtx)
    {
        using var _ = new TraceMethod(valueCtx);

        if (valueCtx is null) {
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
        else if (valueCtx is VsICSContext vsics) {
            return GetCallInvocationExpression(vsics.implicitCallStmt_InStmt());
        }
        else if (valueCtx is VsLiteralContext literal) {
            return GetLiteralSyntax(literal.literal());
        }
        else {
            return ParseExpression(_legacy.GetValue(valueCtx).ToString());
        }
    }

    static ExpressionSyntax GetLiteralSyntax(LiteralContext lit)
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
            return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToInt16(@short.Symbol.Text)));
        }
        else if (lit.DOUBLELITERAL() is ITerminalNode @double) {
            return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(Convert.ToDouble(@double.GetText().TrimEnd(['&']))));
        }
        else if (lit.STRINGLITERAL() is ITerminalNode @string) {
            string str = @string.Symbol.Text;
            str = str.Trim('"');
            if (str.Contains('\\')) {
                str = str.Replace("\\", "\\\\");
            }
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(str));
        }
        else if (lit.FILENUMBER() is ITerminalNode file) {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(file.GetText().TrimStart('#')));
        }
        else if (lit.COLORLITERAL() is ITerminalNode color) {
            var hex = "0x" + color.Symbol.Text[2..].TrimEnd(['&']).PadLeft(6, '0').PadLeft(8, 'F');
            var col = System.Drawing.Color.FromArgb(Convert.ToInt32(hex, 16));

            if (col.IsNamedColor) {
                return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Color"), IdentifierName("FromName")
                    ))
                    .WithArgumentList(RoslynHelpers.ArgumentList(
                        LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(col.Name))
                    ));
            }
            else if (col.A == 255) {
                return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Color"), IdentifierName("FromArgb")
                    ))
                    .WithArgumentList(RoslynHelpers.ArgumentList(
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.R)),
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.G)),                                
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.B))
                    ));
            }
            else {
                return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("Color"), IdentifierName("FromArgb")
                    ))
                    .WithArgumentList(RoslynHelpers.ArgumentList(
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.A)),
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.R)),
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.G)),
                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(col.B))
                    ));
            }
        }
        else {
            throw NotSupported(lit);
        }
    }


    ArgumentListSyntax GetArgumentList(ArgsCallContext args) => SyntaxFactory.ArgumentList(GetArgumentListCore(args));

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

  
    static TypeSyntax GetType(AsTypeClauseContext asType)
    {
        if (asType == null) {
            return PredefinedType(Token(SyntaxKind.VoidKeyword));
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
