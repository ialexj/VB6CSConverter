using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.ExpressionConverter;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class ClassConverter
{
    public static ClassDeclarationSyntax GetClass(ModuleContext module, ClassContext ctx)
    {
        IEnumerable<SyntaxToken> GetModifiers()
        {
            yield return Token(SyntaxKind.PublicKeyword);
            if (ctx.Static) {
                yield return Token(SyntaxKind.StaticKeyword);
            }
            yield return Token(SyntaxKind.PartialKeyword);
        }

        var c = ClassDeclaration(ctx.Name)
            .WithModifiers(TokenList(GetModifiers()));

        if (module.moduleBody() is ModuleBodyContext body) {
            foreach (var member in body.moduleBodyElement()) {
                foreach (var decl in GetMemberDeclarations(member, ctx)) {
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
        }

        var rewriter = new VBFunctionRewriter();
        c = (ClassDeclarationSyntax)rewriter.Visit(c);
        return c;
    }

    public static IEnumerable<MemberDeclarationSyntax> GetMemberDeclarations(ModuleBodyElementContext e, ClassContext ctx)
    {
        using var _ = new TraceMethod(e);

        IEnumerable<MemberDeclarationSyntax> GetMembers()
        {
            if (e.moduleBlock() is ModuleBlockContext moduleBlock) {
                if (moduleBlock.block() is BlockContext block) {
                    foreach (var stmt in block.blockStmt()) {
                        if (stmt.constStmt() is ConstStmtContext @const) {
                            foreach (var f in GetConstantFields(@const)) {
                                yield return f;
                            }
                        }
                        else if (stmt.variableStmt() is VariableStmtContext var) {
                            foreach (var f in GetVariableFields(var, ctx.Static)) {
                                yield return f;
                            }
                        }
                        else if (stmt.attributeStmt() is AttributeStmtContext attr) {
                            // ignore
                        }
                        else {
                            throw new NotSupportedException("Unknown class member declaration");
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
                yield return GetMethod(sub, ctx);
            }
            else if (e.functionStmt() is FunctionStmtContext func) {
                yield return GetMethod(func, ctx);
            }
            else if (e.declareStmt() is DeclareStmtContext declare) {
                yield return GetExtern(declare);
            }
            else if (e.propertyAccessor() is IPropertyContext prop) {
                yield return GetProperty(prop, ctx);
            }
            else if (e.eventStmt() is EventStmtContext @event) {
                yield return GetEvent(@event, ctx);
            }

            else if (e.macroConstStmt() is MacroConstStmtContext macroConst) {
                // todo
            }

            else {
                throw new NotSupportedException("Unknown member declaration");
            }
        }

        try {
            return [.. GetMembers()];
        }
        catch (NotSupportedException nse) {
            return [ 
                ParseMemberDeclaration(e.GetText()).WithError(TransformError.NotSupported(e, nse.Message))
            ];
        }
    }


    public static SyntaxTokenList GetModifiers(IVisibilityContext visibility, bool isStatic, params SyntaxKind[] extra)
    {
        IEnumerable<SyntaxToken> GetTokens() {
            yield return visibility.GetVisibility();
            if (isStatic) {
                yield return Token(SyntaxKind.StaticKeyword);
            }
            foreach (var t in extra) {
                yield return Token(t);
            }
        }

        return TokenList(GetTokens());
    }

    public static IEnumerable<FieldDeclarationSyntax> GetConstantFields(ConstStmtContext @const)
        => DeclarationConverter.GetConstantDeclarations(@const).Select(v => FieldDeclaration(v)
            .WithModifiers(TokenList(
                @const.publicPrivateGlobalVisibility().GetVisibility(),
                Token(SyntaxKind.ConstKeyword))));

    public static IEnumerable<FieldDeclarationSyntax> GetVariableFields(VariableStmtContext var, bool isStatic)
        => DeclarationConverter.GetVariableDeclarations(var).Select(v => FieldDeclaration(v)
            .WithModifiers(GetModifiers(var.visibility(), isStatic)));

    
    public static StructDeclarationSyntax GetStruct(TypeStmtContext type)
    {
        using var _ = new TraceMethod(type);

        return StructDeclaration(GetIdentifier(type.ambiguousIdentifier()))
            .WithModifiers(TokenList(type.visibility().GetVisibility()))
            .WithMembers(
                List<MemberDeclarationSyntax>(
                    type.typeStmt_Element().Select(e =>
                        FieldDeclaration(
                            VariableDeclaration(CommonConverter.ToTypeSyntax(e.asTypeClause()))
                                .WithVariables(
                                    SingletonSeparatedList(
                                        VariableDeclarator(GetIdentifier(e.ambiguousIdentifier())))))
                        .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    )
                )
            );
    }

    public static EnumDeclarationSyntax GetEnum(EnumerationStmtContext e)
    {
        using var _ = new TraceMethod(e);

        return EnumDeclaration(GetIdentifier(e.ambiguousIdentifier()))
            .WithModifiers(TokenList(e.publicPrivateVisibility().GetVisibility()))
            .WithMembers(
                SeparatedList(
                    e.enumerationStmt_Constant().Select(c => {
                        var m = EnumMemberDeclaration(GetIdentifier(c.ambiguousIdentifier()));
                        
                        if (c.valueStmt() is ValueStmtContext v) {
                            m = m.WithEqualsValue(EqualsValueClause(GetValue(v, default)));
                        }

                        return m;
                    }))
                );
    }


    public static MethodDeclarationSyntax GetMethod(IMethodContext methodCtx, ClassContext ctx)
    {
        using var _ = new TraceMethod(methodCtx);

        var type = CommonConverter.ToTypeSyntax(methodCtx.asTypeClause());
        if (methodCtx.IsFunction && type.IsKind(SyntaxKind.VoidKeyword)) {
            type = PredefinedType(Token(SyntaxKind.ObjectKeyword));
        }

        var name = GetIdentifier(methodCtx.ambiguousIdentifier());
        var body = StatementConverter.GetBlock(methodCtx.block(), default);

        var method = MethodDeclaration(type, name)
            .WithModifiers(GetModifiers(methodCtx.visibility(), ctx.Static))
            .WithParameterList(GetMethodParameters(methodCtx.argList()))
            .WithBody(body);

        // Rewrite return style
        var mr = new ReturnValueRewriter();
        method = (MethodDeclarationSyntax)mr.Visit(method);
        return method;
    }

    public static PropertyDeclarationSyntax GetProperty(IPropertyContext propCtx, ClassContext ctx)
    {
        var name = GetIdentifier(propCtx.ambiguousIdentifier());
        var body = StatementConverter.GetBlock(propCtx.block(), default);

        SyntaxKind kind;
        TypeSyntax type;

        if (propCtx is PropertyGetStmtContext get) {
            kind = SyntaxKind.GetAccessorDeclaration;
            type = CommonConverter.ToTypeSyntax(get.asTypeClause());
        }
        else if (propCtx is IPropertySetContext set) {
            kind = SyntaxKind.SetAccessorDeclaration;

            var parameters = GetMethodParameters(set.argList());
            if (parameters.Parameters.Count != 1) {
                throw new NotSupportedException("Expected one parameter for setter.");
            }

            type = parameters.Parameters[0].Type;

            var identifier = parameters.Parameters[0].Identifier.ValueText;
            if (!Equals(identifier, "value")) {
                var renamer = new SimpleIdentifierRenamer(identifier, "value");
                body = (BlockSyntax)renamer.Visit(body);
            }
        }
        else {
            throw new NotSupportedException("Unknown property accessor");
        }

        var prop = PropertyDeclaration(type, name)
            .WithModifiers(GetModifiers(propCtx.visibility(), ctx.Static || propCtx.STATIC() is not null))
            .WithAccessorList(AccessorList(
                SingletonList(AccessorDeclaration(kind)
                    .WithBody(body))));

        var mr = new ReturnValueRewriter();
        prop = (PropertyDeclarationSyntax)mr.Visit(prop);
        return prop;
    }

    public static MethodDeclarationSyntax GetExtern(DeclareStmtContext declare)
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
            CommonConverter.ToTypeSyntax(declare.asTypeClause()),
            GetIdentifier(declare.ambiguousIdentifier()))
            .WithModifiers(GetModifiers(declare.visibility(), true, SyntaxKind.ExternKeyword))
            .WithParameterList(GetMethodParameters(declare.argList()))
            .WithAttributeLists(SingletonList(AttributeList(SingletonSeparatedList(
                Attribute(IdentifierName("DllImport"))
                    .WithArgumentList(AttributeArgumentList(SeparatedList(GetAttributeArguments())))
            ))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            .WithAdditionalAnnotations(new SyntaxAnnotation("Using", "System.Runtime.InteropServices"));
    }

    public static EventDeclarationSyntax GetEvent(EventStmtContext eventCtx, ClassContext ctx)
    {
        using var _ = new TraceMethod(eventCtx);

        var id = GetIdentifierName(eventCtx.ambiguousIdentifier());

        var declr = EventDeclaration(ParseTypeName("EventHandler"), id.Identifier)
            .WithModifiers(GetModifiers(eventCtx.visibility(), ctx.Static))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        var args = GetMethodParameters(eventCtx.argList());
        if (args.ChildNodes().Any()) {
            declr = declr.WithError(TransformError.NotSupported(eventCtx.argList(), "Event with parameters"));
        }

        return declr;
    }


    public static ParameterListSyntax GetMethodParameters(ArgListContext argsContext)
    {
        using var _ = new TraceMethod(argsContext);

        return ParameterList(
            SeparatedList(
                argsContext.arg().Select(GetParameter).ToArray()));
    }

    public static ParameterSyntax GetParameter(ArgContext arg)
    {
        var parameter = Parameter(GetIdentifier(arg.ambiguousIdentifier()))
            .WithType(CommonConverter.ToTypeSyntax(arg.asTypeClause()));

        if (arg.argDefaultValue() is ArgDefaultValueContext def) {
            parameter = parameter.WithDefault(EqualsValueClause(GetValue(def.valueStmt(), default)));
        }

        if (arg.PARAMARRAY() is not null) {
            parameter = parameter
                .WithModifiers(TokenList(Token(SyntaxKind.ParamsKeyword)))
                .WithType(ArrayType(parameter.Type));
        }

        return parameter;
    }
}
