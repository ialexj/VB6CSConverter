using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using VB6Converter.Rewriters;
using VB6Parser;
using VB6Parser.Frx;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.Conversion.CommonConverter;
using static VB6Converter.Conversion.ValueConverter;
using static VB6Converter.RoslynHelpers;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;
public static class ClassConverter
{
    static T AddGeneratedFromComment<T>(T declaration, string sourceRelativePath, int? line = null)
        where T : MemberDeclarationSyntax
    {
        if (sourceRelativePath is not { Length: > 0 }) {
            return declaration;
        }

        var source = line.HasValue
            ? $"{sourceRelativePath}:{line.Value}"
            : sourceRelativePath;
        var generatedTrivia = TriviaList(Comment($"// Generated from: {source}"), EndOfLine("\n"));
        return (T)declaration.WithLeadingTrivia(generatedTrivia.AddRange(declaration.GetLeadingTrivia()));
    }

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

                        c = c.AddBaseListTypes(
                            SimpleBaseType(ParseName("System.Collections.IEnumerable")),
                            SimpleBaseType(ParseTypeName("System.Collections.Generic.IEnumerable<dynamic>")));
                        c = c.AddMembers(
                            MethodDeclaration(ParseName("System.Collections.IEnumerator"), "GetEnumerator")
                                .WithExplicitInterfaceSpecifier(ExplicitInterfaceSpecifier(ParseName("System.Collections.IEnumerable")))
                                .WithExpressionBody(ArrowExpressionClause(InvocationExpression(IdentifierName("GetEnumerator"), ArgumentList())))
                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                        );
                        c = c.AddMembers(
                            MethodDeclaration(ParseTypeName("System.Collections.Generic.IEnumerator<dynamic>"), "GetEnumerator")
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
            var root = GetControl(controlCtx, ctx.SourceDirectory, ctx.OutputDirectory);
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

        return AddGeneratedFromComment(c, ctx.SourceRelativePath);
    }

    public static ClassControlInfo GetControl(ControlPropertiesContext control, string sourceDirectory = null, string outputDirectory = null)
    {
        // Build the FRX offset → byteLength map once from the whole form's control
        // tree (root), then thread it unchanged through every recursive call below.
        // Building it per-control instead would scope the offset sort to only that
        // control's own properties, making the last offset within that narrow view
        // incorrectly extend to end-of-file instead of to the next sibling/child
        // control's offset elsewhere in the form.
        var frxOffsetMap = FrxOffsetScanner.BuildOffsetMap(control, sourceDirectory);
        return GetControl(control, frxOffsetMap, sourceDirectory, outputDirectory);
    }

    private static ClassControlInfo GetControl(
        ControlPropertiesContext control,
        IReadOnlyDictionary<(string filename, int offset), int> frxOffsetMap,
        string sourceDirectory,
        string outputDirectory)
    {
        var name = GetIdentifierName(control.cp_ControlIdentifier().ambiguousIdentifier());
        var type = control.cp_ControlType().complexType().ToTypeSyntax();

        var properties = GetProperties(control.cp_Properties()).ToArray();

        var children = control.cp_Properties().Select(c => c.controlProperties())
            .OfType<ControlPropertiesContext>()
            .Select(c => GetControl(c, frxOffsetMap, sourceDirectory, outputDirectory))
            .ToArray();

        return new ClassControlInfo(type, name) {
            Properties = properties,
            Children = children
        };

        IEnumerable<(ExpressionSyntax target, ExpressionSyntax value)> GetProperties(IEnumerable<Cp_PropertiesContext> properties, ExpressionSyntax parent = null)
        {
            ExpressionSyntax GetFullName(ExpressionSyntax expr)
            {
                if (parent is null) {
                    return expr;
                }

                var combined = parent;
                foreach (var segment in expr.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
                    combined = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, combined, segment);
                }

                return combined;
            }

            bool TryGetSinglePropertyContext(Cp_PropertiesContext prop, out Cp_SinglePropertyContext single)
            {
                single = prop.cp_SingleProperty();
                return single is not null;
            }

            bool TryGetCollectionPattern(Cp_NestedPropertyContext nested, out string collectionPropertyName, out Dictionary<int, Cp_NestedPropertyContext> itemsByIndex)
            {
                collectionPropertyName = null;
                itemsByIndex = null;

                var nestedProperties = nested.cp_Properties();
                if (nestedProperties.Length == 0 || !TryGetSinglePropertyContext(nestedProperties[0], out var countProperty)) {
                    return false;
                }

                var countPropertyExpression = GetCallIdentifierExpression(countProperty.implicitCallStmt_InStmt(), default);
                if (countPropertyExpression is not IdentifierNameSyntax countIdentifier
                    || !countIdentifier.Identifier.Text.StartsWith("Num", StringComparison.Ordinal)
                    || countIdentifier.Identifier.Text.Length <= 3) {
                    return false;
                }

                if (countProperty.cp_PropertyValue()?.literal()?.INTEGERLITERAL() is not ITerminalNode countLiteral
                    || !int.TryParse(countLiteral.GetText(), out var expectedCount)
                    || expectedCount < 0) {
                    return false;
                }

                var remaining = nestedProperties.Skip(1).ToArray();
                if (remaining.Length != expectedCount) {
                    return false;
                }

                var collectionItems = new Dictionary<int, Cp_NestedPropertyContext>();
                foreach (var itemProp in remaining) {
                    if (itemProp.cp_NestedProperty() is not Cp_NestedPropertyContext itemNested) {
                        return false;
                    }

                    if (!TryParseTrailingInteger(itemNested.ambiguousIdentifier().GetText(), out var designerIndex)) {
                        return false;
                    }

                    var zeroBasedIndex = designerIndex - 1;
                    if (zeroBasedIndex < 0 || zeroBasedIndex >= expectedCount || !collectionItems.TryAdd(zeroBasedIndex, itemNested)) {
                        return false;
                    }
                }

                if (collectionItems.Count != expectedCount) {
                    return false;
                }

                collectionPropertyName = countIdentifier.Identifier.Text[3..];
                itemsByIndex = collectionItems;
                return true;
            }

            bool TryParseTrailingInteger(string text, out int value)
            {
                value = 0;
                if (string.IsNullOrEmpty(text)) {
                    return false;
                }

                int digitStart = text.Length;
                while (digitStart > 0 && char.IsDigit(text[digitStart - 1])) {
                    digitStart--;
                }

                if (digitStart == text.Length) {
                    return false;
                }

                return int.TryParse(text[digitStart..], out value);
            }

            foreach (var prop in properties) {
                if (prop.cp_SingleProperty() is Cp_SinglePropertyContext single) {
                    var propName = GetFullName(GetCallIdentifierExpression(single.implicitCallStmt_InStmt(), default));

                    // FRX binary resource reference
                    if (single.FRX_OFFSET() is { } frxToken
                        && single.cp_PropertyValue()?.literal() is LiteralContext frxLiteral) {

                        var frxFilename = GetLiteral(frxLiteral) is LiteralExpressionSyntax lit
                            ? lit.Token.ValueText
                            : null;
                        var hexOffset = frxToken.GetText().TrimStart(':');

                        if (frxFilename is not null
                            && sourceDirectory is not null
                            && outputDirectory is not null
                            && int.TryParse(hexOffset, System.Globalization.NumberStyles.HexNumber, null, out var offsetInt)
                            && frxOffsetMap.TryGetValue((frxFilename, offsetInt), out var byteLength)) {

                            var frxPath = Path.Combine(sourceDirectory, frxFilename);
                            string resolvedResourcePath = null;
                            if (File.Exists(frxPath)) {
                                try {
                                    var item = FrxReader.Read(frxPath, offsetInt, byteLength);
                                    var formName = Path.GetFileNameWithoutExtension(frxFilename);
                                    resolvedResourcePath = FrxResourceExporter.Export(item, formName, outputDirectory);
                                }
                                catch {
                                    // Fall through to unresolved path below
                                }
                            }
                            if (resolvedResourcePath is not null) {
                                yield return (propName, ParseExpression("default").WithFrxResource(resolvedResourcePath));
                                continue;
                            }
                        }

                        // Could not resolve — emit a TODO marker
                        yield return (propName, ParseExpression("default")
                            .WithError(TransformError.Create(single, $"Unresolved FRX resource: {frxFilename}:{hexOffset}")));
                        continue;
                    }

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

                    yield return (propName, valueSyntax);
                }
                else if (prop.cp_NestedProperty() is Cp_NestedPropertyContext nested) {
                    if (TryGetCollectionPattern(nested, out var collectionPropertyName, out var itemsByIndex)) {
                        var collectionName = GetFullName(IdentifierName(collectionPropertyName));

                        foreach (var item in itemsByIndex.OrderBy(k => k.Key)) {
                            var itemName = ElementAccessExpression(
                                collectionName,
                                BracketedArgumentList(SingletonSeparatedList(
                                    Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(item.Key))))));

                            foreach (var itemProperty in GetProperties(item.Value.cp_Properties(), itemName)) {
                                yield return itemProperty;
                            }
                        }

                        continue;
                    }

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

            method = (MemberDeclarationSyntax)ReturnValueRewriter.Default.Visit(method);
            method = (MemberDeclarationSyntax)LabelCollapsingRewriter.Default.Visit(method);
            method = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(method);
        }

        var methodLine = (methodCtx as ParserRuleContext)?.Start?.Line;
        return AddGeneratedFromComment(method, ctx.SourceRelativePath, methodLine);
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

            getter = (MemberDeclarationSyntax)ReturnValueRewriter.Default.Visit(getter);
            getter = (MemberDeclarationSyntax)LabelCollapsingRewriter.Default.Visit(getter);
            getter = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(getter);

            var getTrivia = getter.GetLeadingTrivia().Insert(0, Comment("// VB6 multi-value property getter"));
            getter = getter.WithLeadingTrivia(getTrivia);
            var propertyLine = (propCtx as ParserRuleContext)?.Start?.Line;
            return AddGeneratedFromComment(getter, ctx.SourceRelativePath, propertyLine);
        }

        if (propCtx is IPropertySetContext && parameters.Parameters.Count > 1) {
            var setName = Identifier("Set" + name.Text);
            MemberDeclarationSyntax setter = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), setName)
                .WithModifiers(GetModifiers(propCtx.visibility(), ctx.Static || propCtx.STATIC() is not null))
                .WithParameterList(parameters)
                .WithBody(body);

            setter = (MemberDeclarationSyntax)LabelCollapsingRewriter.Default.Visit(setter);
            setter = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(setter);

            var setTrivia = setter.GetLeadingTrivia().Insert(0, Comment("// VB6 multi-value property setter"));
            setter = setter.WithLeadingTrivia(setTrivia);
            var propertyLine = (propCtx as ParserRuleContext)?.Start?.Line;
            return AddGeneratedFromComment(setter, ctx.SourceRelativePath, propertyLine);
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


        member = (MemberDeclarationSyntax)LabelCollapsingRewriter.Default.Visit(member);
        member = (MemberDeclarationSyntax)TryCatchRewriter.Default.Visit(member);
        member = (MemberDeclarationSyntax)ReturnValueRewriter.Default.Visit(member);
        var memberLine = (propCtx as ParserRuleContext)?.Start?.Line;
        return AddGeneratedFromComment(member, ctx.SourceRelativePath, memberLine);
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
                Attribute(IdentifierName("System.Runtime.InteropServices.DllImport"))
                    .WithArgumentList(AttributeArgumentList(SeparatedList(GetAttributeArguments())))
            ))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
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
        else if (arg.LPAREN() is not null) {
            parameter = parameter
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
