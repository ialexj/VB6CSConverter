using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using VB6Parser;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class ClassConverter
{
    class ControlInfo(TypeSyntax type, IdentifierNameSyntax name)
    {
        public IdentifierNameSyntax Name { get; internal set; } = name;

        public TypeSyntax Type { get; internal set; } = type;

        public IEnumerable<(NameSyntax name, ExpressionSyntax value)> Properties { get; set; } = [];

        public IEnumerable<ControlInfo> Children { get; set; } = [];

        public IdentifierNameSyntax GetIndexedName() 
            => GetArrayIndex() is LiteralExpressionSyntax literal
                ? IdentifierName(Name.Identifier.Text + literal.Token.Text)
                : Name;

        public LiteralExpressionSyntax GetArrayIndex()
            => Properties.FirstOrDefault(p => p.name is IdentifierNameSyntax id && id.Identifier.Text == "Index")
                .value as LiteralExpressionSyntax;

        public FieldDeclarationSyntax GetField() 
            => FieldDeclaration(
                VariableDeclaration(Type,
                    SingletonSeparatedList(VariableDeclarator(GetIndexedName().Identifier)
                        .WithInitializer(EqualsValueClause(
                            ObjectCreationExpression(Type)
                                .WithArgumentList(ArgumentList()))))));

        public IEnumerable<ControlInfo> FlattenControls()
            => new[] { this }.Concat(Children.SelectMany(c => c.FlattenControls()));

        
        public IEnumerable<FieldDeclarationSyntax> GetFields() => FlattenControls().Select(c => c.GetField());

        public IEnumerable<StatementSyntax> GetAssignments()
        {
            bool isFirst = true;
            foreach (var prop in Properties) {
                if (prop.name is IdentifierNameSyntax id && id.Identifier.Text == "Index") {
                    continue;
                }

                NameSyntax name = GetIndexedName();
                foreach (var segment in prop.name.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
                    name = QualifiedName(name, segment);
                }

                var stmt = ExpressionStatement(
                    AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        name, prop.value));

                if (isFirst) {
                    stmt = stmt.WithLeadingTrivia(TriviaList(Comment($"{Environment.NewLine}// {name}")));
                    isFirst = false;
                }

                yield return stmt;
            }

            foreach (var child in Children) {
                foreach (var stmt in child.GetAssignments()) {
                    yield return stmt;
                }
            }
        }
        
        public IEnumerable<(FieldDeclarationSyntax variable, StatementSyntax[] initializers)> GetArrays()
        {
            var arrayChildren = FlattenControls()
                .Where(c => c.GetArrayIndex() != null)
                .Select(c => new { Control = c, Index = (int)c.GetArrayIndex().Token.Value })
                .GroupBy(c => c.Control.Name.Identifier.Text, v => v, 
                    (k, v) => new { Name = k, Controls = v.ToDictionary(k => k.Index, v => v.Control) });

            bool isFirst = true;

            foreach (var array in arrayChildren) {
                var maxIndex = array.Controls.Max(c => c.Key);
                var first    = array.Controls.Values.First();
                var baseType = first.Type;
                var baseName = first.Name;

                var arrayType = ArrayType(baseType)
                    .WithRankSpecifiers(SingletonList(
                        ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                            OmittedArraySizeExpression())
                        )));

                var variable = FieldDeclaration(
                    VariableDeclaration(arrayType, SingletonSeparatedList(
                        VariableDeclarator(baseName.Identifier)
                            .WithInitializer(EqualsValueClause(ArrayCreationExpression(
                                arrayType.WithRankSpecifiers(SingletonList(
                                    ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                                        LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(maxIndex + 1))
                                    ))
                                ))
                            )))
                    ))
                );

                if (isFirst) {
                    variable = variable.WithLeadingTrivia(TriviaList(Whitespace(Environment.NewLine)));
                }

                var initializers = array.Controls.OrderBy(c => c.Key).Select(c => ExpressionStatement(
                    AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        ElementAccessExpression(
                            baseName, BracketedArgumentList(SingletonSeparatedList(
                                Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(c.Key)))
                            ))
                        ),
                        c.Value.GetIndexedName()
                    )
                )).ToArray();

                if (isFirst) {
                    initializers[0] = initializers[0].WithLeadingTrivia(TriviaList(Comment($"{Environment.NewLine}// {array.Name}")));
                }

                yield return (variable, initializers);
                isFirst = false;
            }
        }
    }

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

        if (module.controlProperties() is ControlPropertiesContext controlCtx) {
            var root = GetControl(controlCtx);
            root.Name = IdentifierName("this");

            var variables = root.GetFields().Skip(1).ToArray(); // skip self
            var arrays = root.GetArrays();

            c = c.AddMembers([.. variables.Select(v => v.WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))) ]);

            c = c.AddMembers([.. arrays.Select(v => v.variable.WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))) ]);

            c = c.AddMembers(
                MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), "InitializeComponent")
                    .WithModifiers(TokenList(Token(SyntaxKind.ProtectedKeyword)))
                    .WithBody(Block()
                        .WithStatements(List(root.GetAssignments().Concat(arrays.SelectMany(a => a.initializers))))));

            ControlInfo GetControl(ControlPropertiesContext control)
            {
                var name = GetIdentifierName(control.cp_ControlIdentifier().ambiguousIdentifier());
                var type = control.cp_ControlType().complexType().ToTypeSyntax();

                var properties = GetProperties(control.cp_Properties()).ToArray();

                var children = control.cp_Properties().Select(c => c.controlProperties())
                    .OfType<ControlPropertiesContext>()
                    .Select(c => GetControl(c))
                    .ToArray();

                return new ControlInfo(type, name) {
                    Properties = properties,
                    Children = children
                };
            }

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
                                throw new NotSupportedException("Unknown property value");
                            }
                        }
                        else {
                            throw new NotSupportedException("Property without value");
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

        var type = CommonConverter.ToTypeSyntax(methodCtx.asTypeClause(), methodCtx.IsFunction);
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
            type = CommonConverter.ToTypeSyntax(get.asTypeClause(), true);
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
            .WithType(CommonConverter.ToTypeSyntax(arg.asTypeClause(), true));

        if (arg.argDefaultValue() is ArgDefaultValueContext def) {
            parameter = parameter.WithDefault(EqualsValueClause(GetValue(def.valueStmt(), default)));
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
