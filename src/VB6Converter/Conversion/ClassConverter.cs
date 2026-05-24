using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VB6Converter.Rewriters;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Converter.RoslynHelpers;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class ClassConverter
{
    public static ClassDeclarationSyntax GetClass(ModuleContext module, ClassContext ctx)
    {
        var c = ClassDeclaration(ctx.Name)
            .WithModifiers(Modifiers(isPublic: true, isStatic: ctx.Static, isPartial: true));

        // Main body
        if (module.moduleBody() is ModuleBodyContext body) {
            foreach (var member in body.moduleBodyElement()) {
                if (member.propertyGetStmt() is PropertyGetStmtContext propGet) {
                    // Implement IEnumerable
                    if (propGet.ambiguousIdentifier()?.GetText() == "NewEnum"
                        && propGet.asTypeClause()?.type()?.GetText() == "IUnknown") {

                        ExpressionSyntax bodyExpression = ThrowExpression(ObjectCreationExpression(IdentifierName("System.NotImplementedException"), ArgumentList(), default));
                        var prop = GetProperty(propGet, ctx) as PropertyDeclarationSyntax;
                        if (prop.AccessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))?.ExpressionBody?.Expression is MemberAccessExpressionSyntax elcc) {
                            bodyExpression = InvocationExpression(elcc.WithName(IdentifierName("GetEnumerator")), ArgumentList());
                        }

                        c = c.AddBaseListTypes(SimpleBaseType(ParseName("System.Collections.IEnumerable")));
                        c = c.AddMembers(
                            MethodDeclaration(ParseName("System.Collections.IEnumerator"), "GetEnumerator")
                                .WithModifiers(Modifiers(isPublic: true, isStatic: false))
                                .WithExpressionBody(ArrowExpressionClause(bodyExpression))
                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                        );
                    }
                }


                foreach (var decl in GetMembers(member, ctx)) {
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

        // Control properties
        if (module.controlProperties() is ControlPropertiesContext controlCtx) {
            var root = GetControl(controlCtx);
            root.Name = IdentifierName("this");

            // Base class
            c = c.WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(root.Type))));

            // Instance
            c = c.AddMembers(
                FieldDeclaration(default,
                    Modifiers(isPublic: true, isStatic: true, isReadOnly: true),
                    VariableDeclaration(
                        IdentifierName(ctx.Name), Identifier("_Instance"),
                        ImplicitObjectCreationExpression())
                )
                .WithLeadingTrivia(TriviaList(Trivia(
                    RegionDirectiveTrivia(false)
                        .WithEndOfDirectiveToken(Token(
                            TriviaList(PreprocessingMessage("Control Properties")),
                            SyntaxKind.EndOfDirectiveToken, TriviaList()
                        ))
                )))
            );

            // Variables
            c = c.AddMembers([.. root.GetFields().Skip(1)]); // skip "this"

            // Arrays
            var arrays = root.GetArrays();
            c = c.AddMembers([.. arrays.Select(v => v.variable.WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))) ]);
            c = c.AddMembers(
                MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), "InitializeComponent")
                    .WithModifiers(Modifiers(isProtected: true))
                    .WithBody(Block(List(
                        root.GetAssignments().Concat(arrays.SelectMany(a => a.initializers))
                    )))
                    .WithTrailingTrivia(
                        TriviaList(Trivia(EndRegionDirectiveTrivia(true)))
                    )
            );
        }



        return c;
    }

    public static ClassControlInfo GetControl(ControlPropertiesContext control)
    {
        var name = GetIdentifierName(control.cp_ControlIdentifier().ambiguousIdentifier());
        var type = control.cp_ControlType().complexType().ToTypeSyntax();

        var properties = GetProperties(control.cp_Properties()).ToArray();

        var children = control.cp_Properties().Select(c => c.controlProperties())
            .OfType<ControlPropertiesContext>()
            .Select(GetControl)
            .ToArray();

        return new ClassControlInfo(type, name) {
            Properties = properties,
            Children = children
        };

        IEnumerable<(NameSyntax name, ExpressionSyntax value)> GetProperties(IEnumerable<Cp_PropertiesContext> properties, NameSyntax parent = null)
        {
            NameSyntax GetFullName(NameSyntax expr) => parent is not null ? parent.ToName().AppendName(expr.ToName()) : expr;

            foreach (var prop in properties) {
                if (prop.cp_SingleProperty() is Cp_SinglePropertyContext single) {
                    var name = GetFullName(GetCallIdentifierExpression(single.implicitCallStmt_InStmt(), default).ToName());

                    ExpressionSyntax valueSyntax;
                    if (single.cp_PropertyValue() is Cp_PropertyValueContext valueCtx) {
                        if (valueCtx.literal() is LiteralContext literal) {
                            valueSyntax = GetLiteral(literal);
                        }
                        else if (valueCtx.ambiguousIdentifier() is AmbiguousIdentifierContext amb) {
                            valueSyntax = GetIdentifierName(amb);
                        }
                        else {
                            valueSyntax = ParseExpression("default")
                                .WithError(TransformError.Create(single, "Unknown property value"));
                        }
                    }
                    else {
                        valueSyntax = ParseExpression("default")
                            .WithError(TransformError.Create(single, "Property without value"));
                    }

                    yield return (name, valueSyntax);
                }
                else if (prop.cp_NestedProperty() is Cp_NestedPropertyContext nested) {
                    var name = GetFullName(GetIdentifierName(nested.ambiguousIdentifier()));

                    foreach (var np in GetProperties(nested.cp_Properties(), name)) {
                        yield return np;
                    }
                }
            }
        }
    }

    public static IEnumerable<MemberDeclarationSyntax> GetMembers(ModuleBodyElementContext e, ClassContext ctx)
    {
        using var _ = new TraceMethod(e);

        IEnumerable<MemberDeclarationSyntax> GetMembers()
        {
            if (e.moduleBlock() is ModuleBlockContext moduleBlock) {
                if (moduleBlock.block() is BlockContext block) {
                    foreach (var stmt in block.blockStmt()) {
                        if (stmt.constStmt() is ConstStmtContext @const) {
                            foreach (var f in GetConstantFields(@const, ctx.Options)) {
                                yield return f;
                            }
                        }
                        else if (stmt.variableStmt() is VariableStmtContext var) {
                            foreach (var f in GetVariableFields(var, ctx.Static, ctx.Options)) {
                                yield return f;
                            }
                        }
                        else if (stmt.attributeStmt() is AttributeStmtContext attr) {
                            // ignore
                        }
                        else {
                            yield return GetErrorField(stmt, "Unknown class member declaration", ctx.UseDynamic);
                        }
                    }
                }
            }

            else if (e.enumerationStmt() is EnumerationStmtContext enumCtx) {
                yield return GetEnum(enumCtx);
            }
            else if (e.typeStmt() is TypeStmtContext typeCtx) {
                yield return GetStruct(typeCtx, ctx.UseDynamic);
            }

            else if (e.subStmt() is SubStmtContext sub) {
                yield return GetMethod(sub, ctx);
            }
            else if (e.functionStmt() is FunctionStmtContext func) {
                yield return GetMethod(func, ctx);
            }
            else if (e.declareStmt() is DeclareStmtContext declare) {
                yield return GetExtern(declare, ctx.UseDynamic);
            }
            else if (e.propertyAccessor() is IPropertyContext prop) {
                if (prop is PropertyGetStmtContext getter
                    && prop.ambiguousIdentifier().GetText() == "NewEnum"
                    && getter.asTypeClause().type().GetText() == "IUnknown") {
                    yield break;
                }
                else {
                    yield return GetProperty(prop, ctx);
                }
            }
            else if (e.eventStmt() is EventStmtContext @event) {
                yield return GetEvent(@event, ctx);
            }

            else if (e.macroConstStmt() is MacroConstStmtContext macroConst) {
                // todo
            }

            else {
                yield return GetErrorField(e, "Unknown member declaration", ctx.UseDynamic);
            }
        }

        return [.. GetMembers()];
    }

    static FieldDeclarationSyntax GetErrorField(IParseTree ctx, string message, bool useDynamic = true) =>
        FieldDeclaration(
            VariableDeclaration(useDynamic ? IdentifierName("dynamic") : PredefinedType(Token(SyntaxKind.ObjectKeyword)))
                .WithVariables(SingletonSeparatedList(VariableDeclarator("_unknown"))))
        .WithError(TransformError.Create(ctx, message));


    public static SyntaxTokenList GetModifiers(IVisibilityContext visibility, bool isStatic, params SyntaxKind[] extra)
    {
        IEnumerable<SyntaxToken> GetTokens() {
            yield return visibility.GetVisibility(isStatic ? SyntaxKind.PublicKeyword : SyntaxKind.PrivateKeyword);
            if (isStatic) {
                yield return Token(SyntaxKind.StaticKeyword);
            }
            foreach (var t in extra) {
                yield return Token(t);
            }
        }

        return TokenList(GetTokens());
    }

    public static IEnumerable<FieldDeclarationSyntax> GetConstantFields(ConstStmtContext @const, ConversionOptions options = null)
        => DeclarationConverter.GetConstantDeclarations(@const, options).Select(v => FieldDeclaration(v)
            .WithModifiers(TokenList(
                @const.publicPrivateGlobalVisibility().GetVisibility(),
                Token(SyntaxKind.ConstKeyword))));

    public static IEnumerable<FieldDeclarationSyntax> GetVariableFields(VariableStmtContext var, bool isStatic, ConversionOptions options = null)
        => DeclarationConverter.GetVariableDeclarations(var, options: options).Select(v => FieldDeclaration(v)
            .WithModifiers(GetModifiers(var.visibility(), isStatic)));


    public static StructDeclarationSyntax GetStruct(TypeStmtContext type, bool useDynamic = true)
    {
        using var _ = new TraceMethod(type);

        return StructDeclaration(GetIdentifier(type.ambiguousIdentifier()))
            .WithModifiers(TokenList(type.visibility().GetVisibility()))
            .WithMembers(
                List<MemberDeclarationSyntax>(
                    type.typeStmt_Element().Select(e =>
                        FieldDeclaration(
                            VariableDeclaration(CommonConverter.ToTypeSyntax(e.asTypeClause(), useDynamic: useDynamic))
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


    public static MemberDeclarationSyntax GetMethod(IMethodContext methodCtx, ClassContext ctx)
    {
        using var _ = new TraceMethod(methodCtx);

        var type = CommonConverter.ToTypeSyntax(methodCtx.asTypeClause(), methodCtx.IsFunction, ctx.UseDynamic);
        var name = GetIdentifier(methodCtx.ambiguousIdentifier());
        var body = StatementConverter.GetBlock(methodCtx.block(), new CallContext(null, ctx.Options));

        MemberDeclarationSyntax method;
        if (name.Text == "Class_Initialize") {
            method = ConstructorDeclaration(ctx.Name)
                .WithModifiers(Modifiers(isPublic: true))
                .WithParameterList(GetMethodParameters(methodCtx.argList(), ctx.UseDynamic))
                .WithBody(body);
        }
        else {
            method = MethodDeclaration(type, name)
                .WithModifiers(GetModifiers(methodCtx.visibility(), ctx.Static))
                .WithParameterList(GetMethodParameters(methodCtx.argList(), ctx.UseDynamic))
                .WithBody(body);

            method = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(method);
            method = (MemberDeclarationSyntax)ReturnValueRewriter.Default.Visit(method);
        }

        return method;
    }

    public static MemberDeclarationSyntax GetProperty(IPropertyContext propCtx, ClassContext ctx)
    {
        using var _ = new TraceMethod(propCtx);

        var type = ctx.UseDynamic ? (TypeSyntax)IdentifierName("dynamic") : PredefinedType(Token(SyntaxKind.ObjectKeyword));
        var name = GetIdentifier(propCtx.ambiguousIdentifier());
        var body = StatementConverter.GetBlock(propCtx.block(), new CallContext(null, ctx.Options));
        var parameters = GetMethodParameters(propCtx.argList(), ctx.UseDynamic);

        // Multi-value (parameterized) property: no direct C# equivalent, emit as methods.
        if (propCtx is PropertyGetStmtContext getMulti && parameters.Parameters.Count > 0) {
            var retType = CommonConverter.ToTypeSyntax(getMulti.asTypeClause(), true, ctx.UseDynamic);
            MemberDeclarationSyntax getter = MethodDeclaration(retType, name)
                .WithModifiers(GetModifiers(propCtx.visibility(), ctx.Static || propCtx.STATIC() is not null))
                .WithParameterList(parameters)
                .WithBody(body);
            getter = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(getter);
            getter = (MemberDeclarationSyntax)ReturnValueRewriter.Default.Visit(getter);
            var getTrivia = getter.GetLeadingTrivia().Insert(0, Comment("// VB6 multi-value property getter"));
            return getter.WithLeadingTrivia(getTrivia);
        }

        if (propCtx is IPropertySetContext && parameters.Parameters.Count > 1) {
            var setName = Identifier("Set" + name.Text);
            MemberDeclarationSyntax setter = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), setName)
                .WithModifiers(GetModifiers(propCtx.visibility(), ctx.Static || propCtx.STATIC() is not null))
                .WithParameterList(parameters)
                .WithBody(body);
            setter = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(setter);
            var setTrivia = setter.GetLeadingTrivia().Insert(0, Comment("// VB6 multi-value property setter"));
            return setter.WithLeadingTrivia(setTrivia);
        }

        SyntaxKind kind;

        if (propCtx is PropertyGetStmtContext get) {
            kind = SyntaxKind.GetAccessorDeclaration;
            type = CommonConverter.ToTypeSyntax(get.asTypeClause(), true, ctx.UseDynamic);
        }
        else if (propCtx is IPropertySetContext set) {
            kind = SyntaxKind.SetAccessorDeclaration;

            if (parameters.Parameters.Count > 0) {
                type = parameters.Parameters[0].Type;

                var identifier = parameters.Parameters[0].Identifier.Text;
                if (!Equals(identifier, "value")) {
                    body = (BlockSyntax)new SimpleIdentifierRenamer(identifier, "value").Visit(body);
                }
            }
        }
        else {
            return GetErrorField(propCtx, "Unknown property accessor", ctx.UseDynamic);
        }

        var attr = propCtx.block().blockStmt().Select(b => b.attributeStmt())
            .OfType<AttributeStmtContext>()
            .FirstOrDefault();

        MemberDeclarationSyntax member;
        if (attr != null
            && attr.implicitCallStmt_InStmt().GetText().EndsWith("VB_UserMemId")
            && attr.literal().Length == 1 && attr.literal()[0].INTEGERLITERAL() is ITerminalNode l && l.Symbol.Text == "0") {

            member = IndexerDeclaration(type)
                .WithModifiers(GetModifiers(propCtx.visibility(), false))
                .WithParameterList(BracketedParameterList(SingletonSeparatedList(
                    Parameter(parameters.Parameters[0].Identifier)
                        .WithType(ctx.UseDynamic ? (TypeSyntax)IdentifierName("dynamic") : PredefinedType(Token(SyntaxKind.ObjectKeyword)))
                )))
                .WithAccessorList(AccessorList(
                    SingletonList(AccessorDeclaration(kind)
                        .WithBody(body))));

            member = (MemberDeclarationSyntax)new ReturnValueRewriter(name).Visit(member);
        }
        else {
            member = PropertyDeclaration(type, name)
                .WithModifiers(GetModifiers(propCtx.visibility(), ctx.Static || propCtx.STATIC() is not null))
                .WithAccessorList(AccessorList(
                    SingletonList(AccessorDeclaration(kind)
                        .WithBody(body))));
        }


        member = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(member);
        member = (MemberDeclarationSyntax)ReturnValueRewriter.Default.Visit(member);
        return member;
    }

    public static MethodDeclarationSyntax GetExtern(DeclareStmtContext declare, bool useDynamic = true)
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
            CommonConverter.ToTypeSyntax(declare.asTypeClause(), useDynamic: useDynamic),
            GetIdentifier(declare.ambiguousIdentifier()))
            .WithModifiers(GetModifiers(declare.visibility(), true, SyntaxKind.ExternKeyword))
            .WithParameterList(GetMethodParameters(declare.argList(), useDynamic))
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
            declr = declr.WithError(TransformError.Create(eventCtx.argList(), "Event with parameters"));
        }

        return declr;
    }


    public static ParameterListSyntax GetMethodParameters(ArgListContext argsContext, bool useDynamic = true)
    {
        using var _ = new TraceMethod(argsContext);

        return ParameterList(
            SeparatedList(
                argsContext.arg().Select(a => GetParameter(a, useDynamic)).ToArray()));
    }

    public static ParameterSyntax GetParameter(ArgContext arg, bool useDynamic = true)
    {
        var parameter = Parameter(GetIdentifier(arg.ambiguousIdentifier()))
            .WithType(CommonConverter.ToTypeSyntax(arg.asTypeClause(), true, useDynamic));


        if (arg.argDefaultValue() is ArgDefaultValueContext def) {
            parameter = parameter.WithDefault(EqualsValueClause(GetValue(def.valueStmt(), default)));
        }
        else if (arg.OPTIONAL() is not null) {
            parameter = parameter.WithDefault(EqualsValueClause(
                LiteralExpression(SyntaxKind.DefaultLiteralExpression, Token(SyntaxKind.DefaultKeyword))));
        }

        if (arg.PARAMARRAY() is not null) {
            parameter = parameter
                .WithModifiers(TokenList(Token(SyntaxKind.ParamsKeyword)))
                .WithType(ArrayType(parameter.Type)
                    .WithRankSpecifiers(SingletonList(
                        ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                            OmittedArraySizeExpression()
                        ))
                    )));
        }

        return parameter;
    }
}
