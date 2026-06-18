#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static ComStubGenerator.StubGenHelpers;

namespace ComStubGenerator;

/// <summary>
/// Generates C# stub source files from a <see cref="ComQueryLibrary"/> and writes
/// them under <c>_Reference/{SafeLibraryName}/{TypeName}.cs</c>.
/// </summary>
public static class ReferenceStubGenerator
{
    /// <summary>
    /// Writes stub files for every type in <paramref name="library"/> under
    /// <paramref name="referenceRoot"/> and returns the paths of the files written.
    /// Alias types (TKIND_ALIAS) are not written here; collect them separately via
    /// <see cref="CollectAliases"/> and pass all libraries' aliases to
    /// <see cref="ReferenceUsingsGenerator.Generate"/> for global deduplication.
    /// </summary>
    public static IReadOnlyList<string> Generate(
        ComQueryLibrary library,
        string referenceRoot,
        bool filterComPlumbing = true,
        bool useDynamic = true,
        bool strictParameters = false)
    {
        var written = new List<string>();
        var usedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string libDir = Path.Combine(referenceRoot, library.SafeName);
        Directory.CreateDirectory(libDir);

        var types = library.Types ?? [];

        // Pre-pass: compute emitted names and build the qualified-name → emitted-name map
        // for every struct in this library.  Used by cycle detection below.
        var structQualifiedToEmitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        {
            var prepassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in types.OrderBy(t => t.Name)) {
                if (type.Kind == LibraryTypeKind.Alias) continue;
                string emitted = MakeUniqueName(MakeSafeIdentifier(type.Name), prepassNames);
                if (type.Kind == LibraryTypeKind.Struct)
                    structQualifiedToEmitted[$"{library.SafeName}.{type.Name}"] = emitted;
            }
        }
        var cyclicFields = DetectStructCycles(library, structQualifiedToEmitted);

        foreach (var type in types.OrderBy(t => t.Name)) {
            if (type.Kind == LibraryTypeKind.Alias) continue;  // handled by CollectAliases / ReferenceUsingsGenerator

            string emittedTypeName = MakeUniqueName(MakeSafeIdentifier(type.Name), usedTypeNames);
            string? source = GenerateType(library, type, emittedTypeName, cyclicFields, filterComPlumbing, useDynamic, strictParameters);
            if (source == null) continue;

            string filePath = Path.Combine(libDir, $"{emittedTypeName}.cs");
            File.WriteAllText(filePath, source);
            written.Add(filePath);

            if (type.Kind == LibraryTypeKind.Class &&
                type.Name.Contains("Extender", StringComparison.OrdinalIgnoreCase)) {
                string extensionSource = GenerateExtenderExtensionSource(library, emittedTypeName, source);
                string extensionPath = Path.Combine(libDir, $"{emittedTypeName}Extensions.cs");
                File.WriteAllText(extensionPath, extensionSource);
                written.Add(extensionPath);
            }
        }

        return written;
    }

    /// <summary>
    /// Returns the alias definitions (TKIND_ALIAS) declared in <paramref name="library"/>
    /// without writing any files.  Callers should aggregate aliases from all libraries and
    /// pass them to <see cref="ReferenceUsingsGenerator.Generate"/> so duplicates (the same
    /// alias name from different libraries, e.g. <c>OLE_COLOR</c> from stdole and oleaut32)
    /// are deduplicated into a single <c>global using</c> directive.
    /// </summary>
    public static IReadOnlyList<(string Name, string CSharpType)> CollectAliases(ComQueryLibrary library)
    {
        var aliases = new List<(string Name, string CSharpType)>();
        foreach (var type in library.Types ?? []) {
            if (type.Kind == LibraryTypeKind.Alias && !string.IsNullOrWhiteSpace(type.AliasedType))
                aliases.Add((MakeSafeIdentifier(type.Name), type.AliasedType!));
        }
        return aliases;
    }

    // Member names from IUnknown/IDispatch that are filtered by ComPlumbingFilterRewriter on the child stubs
    // and must also be skipped when generating static forwarders in __AppObjects.
    static readonly HashSet<string> AppObjectsBlockedMethodNames = new(StringComparer.Ordinal)
    {
        "AddRef", "Release", "QueryInterface",
        "GetIDsOfNames", "GetTypeInfo", "GetTypeInfoCount",
        "Equals", "GetHashCode", "GetType", "ToString",
    };
    static readonly HashSet<string> AppObjectsBlockedPropertyNames = new(StringComparer.Ordinal) { "ToString" };

    /// <summary>
    /// Generates a <c>__AppObjects.cs</c> file in <c><paramref name="referenceRoot"/>/{library.SafeName}/</c>
    /// containing a <c>public static class __AppObjects</c> with:
    /// <list type="bullet">
    ///   <item>One <c>public static readonly T Name = new T();</c> field per COM app-object type
    ///   (those marked with <c>TYPEFLAG_FAPPOBJECT</c>), collected across all supplied libraries.</item>
    ///   <item>Static forwarding members for every property and method on each app-object type,
    ///   so that VB6 code that calls members directly (without qualifying via the instance name)
    ///   compiles unchanged after conversion.</item>
    /// </list>
    /// The file is written at <c><paramref name="referenceRoot"/>/__AppObjects.cs</c> (no library subfolder)
    /// with no namespace so that <c>global using static __AppObjects;</c> makes all members globally visible.
    /// Returns the path of the file written, or <see langword="null"/> when no app-object types exist.
    /// </summary>
    public static string? GenerateAppObjects(
        IEnumerable<ComQueryLibrary> libraries,
        string referenceRoot,
        bool useDynamic = true,
        bool strictParameters = false)
    {
        var appObjectTypes = (libraries ?? [])
            .SelectMany(l => (l.Types ?? [])
                .Where(t => t.IsAppObject && t.Kind == LibraryTypeKind.Class))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (appObjectTypes.Count == 0) return null;

        Directory.CreateDirectory(referenceRoot);

        // Tracks names used at the __AppObjects class level to keep them unique.
        var classLevelUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "__AppObjects" };
        var memberDecls = new List<MemberDeclarationSyntax>();

        foreach (var appObjType in appObjectTypes) {
            string safeName = MakeSafeIdentifier(appObjType.Name);
            // Skip if a type with this name was already contributed by a previous library.
            if (!classLevelUsed.Add(safeName)) continue;

            // ── Singleton field ────────────────────────────────────────────
            // public static readonly Screen Screen = new Screen();
            memberDecls.Add(
                FieldDeclaration(
                    VariableDeclaration(ParseTypeName(safeName))
                        .WithVariables(SingletonSeparatedList(
                            VariableDeclarator(Identifier(safeName))
                                .WithInitializer(EqualsValueClause(
                                    ObjectCreationExpression(ParseTypeName(safeName))
                                        .WithArgumentList(ArgumentList()))))))
                    .WithModifiers(Modifiers(isPublic: true, isStatic: true, isReadOnly: true)));

            var members = appObjType.Members ?? [];

            // ── Static forwarding properties ───────────────────────────────
            var propertyGroups = members
                .Where(m => m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet)
                .GroupBy(m => m.Name)
                .ToList();

            foreach (var group in propertyGroups.OrderBy(g => g.Key)) {
                // Skip COM plumbing properties shared with IDispatch/IUnknown.
                if (AppObjectsBlockedPropertyNames.Contains(group.Key)) continue;
                // Skip if another app-object already emitted a forwarder with this name.
                string instanceName = MakeSafeIdentifier(group.Key);
                if (classLevelUsed.Contains(instanceName)) continue;

                var getter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
                var setter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertySet);

                string propType = getter != null ? getter.ReturnType : "object";
                if (propType == "void") propType = "object";

                // The name as it appears on the stub class (mirrors GenerateClass naming) — already computed above.
                // The name exposed as static in __AppObjects (unique at class level).
                string staticName = MakeUniqueName(instanceName, classLevelUsed);

                if (getter?.Parameters.Count > 0) {
                    // Parameterized property → was emitted as a plain method (and Set{Name}) in GenerateClass.
                    var getParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters).ToArray();
                    var getArgs = getter.Parameters
                        .Select(p => Argument(IdentifierName(MakeSafeIdentifier(p.Name)))).ToArray();

                    memberDecls.Add(MethodDeclaration(MemberType(propType, useDynamic), Identifier(staticName))
                        .WithModifiers(Modifiers(isPublic: true, isStatic: true))
                        .WithParameterList(ParameterList(SeparatedList(getParams)))
                        .WithExpressionBody(ArrowExpressionClause(
                            InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(safeName), IdentifierName(instanceName)),
                                ArgumentList(SeparatedList(getArgs)))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

                    if (setter != null) {
                        var setStaticName = "Set" + staticName;
                        classLevelUsed.Add(setStaticName);
                        var setParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters)
                            .Append(BuildSetterValueParam(propType, useDynamic, strictParameters))
                            .ToArray();
                        var setArgs = getter.Parameters
                            .Select(p => Argument(IdentifierName(MakeSafeIdentifier(p.Name))))
                            .Append(Argument(IdentifierName("value"))).ToArray();

                        memberDecls.Add(MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(setStaticName))
                            .WithModifiers(Modifiers(isPublic: true, isStatic: true))
                            .WithParameterList(ParameterList(SeparatedList(setParams)))
                            .WithExpressionBody(ArrowExpressionClause(
                                InvocationExpression(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName(safeName), IdentifierName("Set" + instanceName)),
                                    ArgumentList(SeparatedList(setArgs)))))
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                    }
                }
                else {
                    var accessors = new List<AccessorDeclarationSyntax>();
                    if (getter != null) {
                        accessors.Add(
                            AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithExpressionBody(ArrowExpressionClause(
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                        IdentifierName(safeName), IdentifierName(instanceName))))
                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                    }
                    if (setter != null) {
                        accessors.Add(
                            AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithExpressionBody(ArrowExpressionClause(
                                    AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName(safeName), IdentifierName(instanceName)),
                                        IdentifierName("value"))))
                                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                    }

                    memberDecls.Add(
                        PropertyDeclaration(MemberType(propType, useDynamic), Identifier(staticName))
                            .WithModifiers(Modifiers(isPublic: true, isStatic: true))
                            .WithAccessorList(AccessorList(List(accessors))));
                }
            }

            // ── Static forwarding methods ──────────────────────────────────
            var usedMethodSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in members
                         .Where(m => m.Kind == LibraryMemberKind.Method)
                         .OrderBy(m => m.Name)) {
                // Skip COM plumbing methods shared with IDispatch/IUnknown.
                if (AppObjectsBlockedMethodNames.Contains(method.Name)) continue;
                if (method.Name == "Invoke" && method.Parameters.Count == 8) continue;
                string instanceMethodName = MakeSafeIdentifier(method.Name);
                // Skip if another app-object already emitted a forwarder with this name.
                if (classLevelUsed.Contains(instanceMethodName)) continue;
                string paramSig = string.Join(",", method.Parameters.Select(p => p.Type));
                string staticMethodName = MakeUniqueMethodName(instanceMethodName, paramSig, usedMethodSignatures, classLevelUsed);
                // Claim the base name so cross-type duplicates are skipped rather than renamed.
                classLevelUsed.Add(instanceMethodName);

                var parameters = BuildParameters(method.Parameters, useDynamic, strictParameters: strictParameters).ToArray();
                var args = method.Parameters.Select(p => {
                    var arg = Argument(IdentifierName(MakeSafeIdentifier(p.Name)));
                    if (p.IsOut && !p.IsOptional)
                        arg = arg.WithRefKindKeyword(Token(SyntaxKind.RefKeyword));
                    return arg;
                }).ToArray();

                var callExpr = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(safeName), IdentifierName(instanceMethodName)),
                    ArgumentList(SeparatedList(args)));

                bool isVoid = string.Equals(method.ReturnType, "void", StringComparison.OrdinalIgnoreCase);
                TypeSyntax returnType = isVoid
                    ? (TypeSyntax)PredefinedType(Token(SyntaxKind.VoidKeyword))
                    : MemberType(method.ReturnType, useDynamic);

                memberDecls.Add(
                    MethodDeclaration(returnType, Identifier(staticMethodName))
                        .WithModifiers(Modifiers(isPublic: true, isStatic: true))
                        .WithParameterList(ParameterList(SeparatedList(parameters)))
                        .WithExpressionBody(ArrowExpressionClause(callExpr))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }
        }

        var classDecl = ClassDeclaration(Identifier("__AppObjects"))
            .WithModifiers(Modifiers(isPublic: true, isStatic: true))
            .WithMembers(List(memberDecls));

        // No namespace — class lives in the global namespace so that
        // `global using static __AppObjects;` in _ReferenceUsings.cs exposes all
        // members without requiring a library-specific qualifier.
        var cu = CompilationUnit(
                default,
                default,
                default,
                SingletonList<MemberDeclarationSyntax>(classDecl))
            .NormalizeWhitespace();

        string filePath = Path.Combine(referenceRoot, "__AppObjects.cs");
        File.WriteAllText(filePath, cu.ToFullString());
        return filePath;
    }

    /// <summary>
    /// Writes <c>_ComStubInterfaces.cs</c> to <paramref name="referenceRoot"/> containing
    /// the marker interfaces used as extension targets on all generated stubs:
    /// <list type="bullet">
    ///   <item><c>IComStub</c> — base marker for all COM stubs.</item>
    ///   <item><c>IOleStub : IComStub</c> — marker for coclasses that implement IOleObject.</item>
    ///   <item><c>IControlStub&lt;T&gt; : IComStub</c> — marker for ActiveX control stubs;
    ///         exposes a default <c>T Object =&gt; (T)(object)this</c> property that simulates
    ///         the VB6 extender's <c>Object</c> property (returns the underlying control).</item>
    /// </list>
    /// The file is placed in the global namespace (no namespace wrapper) so that the types
    /// are visible throughout the converted project without an explicit <c>using</c>.
    /// </summary>
    public static string GenerateMarkerInterfaces(string referenceRoot)
    {
        Directory.CreateDirectory(referenceRoot);

        var iComStub = InterfaceDeclaration(Identifier("IComStub"))
            .WithModifiers(Modifiers(isPublic: true));

        var iOleStub = InterfaceDeclaration(Identifier("IOleStub"))
            .WithModifiers(Modifiers(isPublic: true))
            .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                SimpleBaseType(IdentifierName("IComStub")))));

        // IControlStub<T> : IComStub
        //   Simulates the VB6 extender mechanism.  The default `T Object => (T)(object)this`
        //   property lets callers obtain the underlying control instance via the extender's
        //   well-known `Object` property without any runtime overhead.
        //   The double-cast via object is required because inside a default interface member
        //   `this` is typed as the interface, not the type parameter T.
        var objectProperty = PropertyDeclaration(
                IdentifierName("T"),
                Identifier("Object"))
            .WithExpressionBody(ArrowExpressionClause(
                CastExpression(
                    IdentifierName("T"),
                    ParenthesizedExpression(
                        CastExpression(
                            PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                            ThisExpression())))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        var iControlStub = InterfaceDeclaration(Identifier("IControlStub"))
            .WithModifiers(Modifiers(isPublic: true))
            .WithTypeParameterList(TypeParameterList(
                SingletonSeparatedList(
                    TypeParameter(Identifier("T"))
                        .WithVarianceKeyword(Token(SyntaxKind.OutKeyword)))))
            .WithConstraintClauses(
                SingletonList(TypeParameterConstraintClause(
                        IdentifierName("T"))
                    .WithConstraints(SingletonSeparatedList<TypeParameterConstraintSyntax>(
                        ClassOrStructConstraint(SyntaxKind.ClassConstraint)))))
            .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                SimpleBaseType(IdentifierName("IComStub")))))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(objectProperty));

        // [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        // public sealed class IndexedPropertyAttribute : Attribute
        // Marks getter and setter methods emitted from parameterized COM properties so that
        // tooling can distinguish them from regular methods with a similar naming pattern.
        var attributeUsageAttr = Attribute(
            ParseName("System.AttributeUsage"),
            AttributeArgumentList(SeparatedList(new AttributeArgumentSyntax[] {
                AttributeArgument(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        ParseName("System.AttributeTargets"),
                        IdentifierName("Method"))),
                AttributeArgument(
                    LiteralExpression(SyntaxKind.FalseLiteralExpression))
                    .WithNameEquals(NameEquals(IdentifierName("AllowMultiple")))
            })));

        var indexedPropConstructor = ConstructorDeclaration(Identifier("IndexedPropertyAttribute"))
            .WithModifiers(Modifiers(isPublic: true))
            .WithParameterList(ParameterList(SingletonSeparatedList(
                Parameter(Identifier("name"))
                    .WithType(PredefinedType(Token(SyntaxKind.StringKeyword))))))
            .WithExpressionBody(ArrowExpressionClause(
                AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    IdentifierName("Name"),
                    IdentifierName("name"))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        var indexedPropNameProp = PropertyDeclaration(
                PredefinedType(Token(SyntaxKind.StringKeyword)),
                Identifier("Name"))
            .WithModifiers(Modifiers(isPublic: true))
            .WithAccessorList(AccessorList(SingletonList(
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))));

        var indexedPropertyAttrClass = ClassDeclaration(Identifier("IndexedPropertyAttribute"))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword)))
            .WithAttributeLists(SingletonList(
                AttributeList(SingletonSeparatedList(attributeUsageAttr))))
            .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                SimpleBaseType(ParseName("System.Attribute")))))
            .WithMembers(List<MemberDeclarationSyntax>([indexedPropConstructor, indexedPropNameProp]));

        var cu = CompilationUnit(
                default,
                default,
                default,
                List<MemberDeclarationSyntax>([iComStub, iOleStub, iControlStub, indexedPropertyAttrClass]))
            .NormalizeWhitespace();

        string filePath = Path.Combine(referenceRoot, "_ComStubInterfaces.cs");
        File.WriteAllText(filePath, cu.ToFullString());
        return filePath;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Per-type dispatch
    // ──────────────────────────────────────────────────────────────────────

    static string? GenerateType(
        ComQueryLibrary library,
        ComQueryType type,
        string emittedTypeName,
        HashSet<(string TypeName, string FieldName)> cyclicFields,
        bool filterComPlumbing,
        bool useDynamic = true,
        bool strictParameters = false)
    {
        MemberDeclarationSyntax? decl = type.Kind switch {
            LibraryTypeKind.Enum                                              => GenerateEnum(type, emittedTypeName),
            LibraryTypeKind.DispatchInterface or LibraryTypeKind.Interface     => GenerateInterface(library, type, emittedTypeName, useDynamic, strictParameters),
            LibraryTypeKind.Class or LibraryTypeKind.Module                    => GenerateClass(library, type, emittedTypeName, useDynamic, strictParameters),
            LibraryTypeKind.Struct                                            => GenerateStruct(type, emittedTypeName, cyclicFields, useDynamic),
            _                                                                  => null,
        };

        if (decl == null) return null;

        var ns = IdentifierName(library.SafeName);

        var cu = CompilationUnit(
                default,
                default,
                default,
                SingletonList<MemberDeclarationSyntax>(
                    FileScopedNamespaceDeclaration(ns)
                        .WithMembers(SingletonList(decl))))
            .NormalizeWhitespace();

        if (DotnetLibraryGuids.RequiresNormalization(library)) {
            // Post-process: rewrite mscorlib/System.* type references to their canonical .NET
            // equivalents, but only for libraries that actually depend on those type libraries.
            cu = (CompilationUnitSyntax)new MscorlibTypeNormalizingRewriter().Visit(cu)!;

            // Collapse add_X / remove_X method pairs (emitted by .NET components registered in COM)
            // into proper event declarations so interface contracts such as IComponent are satisfied.
            cu = (CompilationUnitSyntax)new AddRemoveEventCollapsingRewriter().Visit(cu)!;

            cu = cu.NormalizeWhitespace();
        }


        // Filter out COM infrastructure interfaces and methods (IUnknown/IDispatch plumbing)
        // unless the caller has opted in to retaining them.
        if (filterComPlumbing) {
            cu = (CompilationUnitSyntax)new ComPlumbingFilterRewriter().Visit(cu)!;

            // If the entire type was removed (e.g. IDispatch, IUnknown), skip writing the file.
            bool hasTypeDeclaration = cu.DescendantNodes()
                .Any(n => n is BaseTypeDeclarationSyntax);
            if (!hasTypeDeclaration) return null;

            cu = cu.NormalizeWhitespace();
        }

        return cu.ToFullString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Enum
    // ──────────────────────────────────────────────────────────────────────

    static EnumDeclarationSyntax GenerateEnum(ComQueryType type, string emittedTypeName)
    {
        bool needsLong = (type.EnumValues ?? []).Any(v => v.Value < int.MinValue || v.Value > int.MaxValue);

        var members = (type.EnumValues ?? [])
            .OrderBy(v => v.Value)
            .Select(v => EnumMemberDeclaration(
                Identifier(MakeSafeIdentifier(v.Name)))
                .WithEqualsValue(
                    EqualsValueClause(
                        LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            needsLong ? Literal(v.Value) : Literal((int)v.Value)))))
            .ToArray<EnumMemberDeclarationSyntax>();

        var decl = EnumDeclaration(Identifier(emittedTypeName))
            .WithModifiers(Modifiers(isPublic: true))
            .WithMembers(SeparatedList(members));

        if (needsLong)
        {
            decl = decl.WithBaseList(
                BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                    SimpleBaseType(PredefinedType(Token(SyntaxKind.LongKeyword))))));
        }

        return decl;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Interface / dispatch interface
    // ──────────────────────────────────────────────────────────────────────

    static InterfaceDeclarationSyntax GenerateInterface(
        ComQueryLibrary library,
        ComQueryType type,
        string emittedTypeName,
        bool useDynamic = true,
        bool strictParameters = false)
    {
        var memberDecls = new List<MemberDeclarationSyntax>();
        // Seed with the interface name to prevent CS0542 (member name same as enclosing type)
        var usedMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };

        var propertyGroups = (type.Members ?? [])
            .Where(m => m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet)
            .GroupBy(m => m.Name)
            .ToList();

        bool hasIndexer = propertyGroups.Any(g => {
            var pg = g.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
            return pg?.IsDefault == true && pg.Parameters.Count > 0;
        });
        var forwardingIndexer = TryBuildDefaultForwardingIndexer(library, type, isForInterface: true, useDynamic, strictParameters);
        bool hasAnyIndexer = hasIndexer || forwardingIndexer is not null;
        foreach (var group in propertyGroups.OrderBy(g => g.Key)) {
            var getter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
            var setter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertySet);

            string propType = getter != null ? getter.ReturnType : "object";
            if (propType == "void") propType = "object";

            var accessors = new List<AccessorDeclarationSyntax>();
            if (getter != null) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }
            if (setter != null) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            // A default member (DISPID 0) with parameters becomes a C# indexer (this[...])
            // so that bang-operator conversions like rs!MyField → rs["MyField"] compile correctly.
            if (getter?.IsDefault == true && getter.Parameters.Count > 0) {
                var indexerParams = BuildParameters(getter.Parameters, useDynamic, skipFirstDefault: true, strictParameters: strictParameters).ToArray();
                var indexer = IndexerDeclaration(MemberType(propType, useDynamic))
                    .WithParameterList(BracketedParameterList(SeparatedList(indexerParams)))
                    .WithAccessorList(AccessorList(List(accessors)));
                memberDecls.Add(indexer);

                // For non-"Item" default properties also emit a named method form so that VB6 code
                // that calls the property by name (e.g. xa.Value(i)) still compiles after
                // conversion via ParameterizedPropertyRewriter.  "Item"-named properties are
                // excluded because the method loop already handles any explicit Item method by
                // renaming it to GetItem — adding another GetItem here would produce a duplicate.
                if (!string.Equals(group.Key, "Item", StringComparison.OrdinalIgnoreCase)) {
                    string getName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);
                    var getParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters).ToArray();
                    memberDecls.Add(MethodDeclaration(MemberType(propType, useDynamic), Identifier(getName))
                        .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                        .WithParameterList(ParameterList(SeparatedList(getParams)))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

                    if (setter != null) {
                        string setName = MakeUniqueName("Set" + MakeSafeIdentifier(group.Key), usedMemberNames);
                        var setParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters)
                            .Append(BuildSetterValueParam(propType, useDynamic, strictParameters))
                            .ToArray();
                        memberDecls.Add(MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(setName))
                            .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                            .WithParameterList(ParameterList(SeparatedList(setParams)))
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                    }
                }
            }
            else if (getter?.Parameters.Count > 0) {
                // C# has no parameterized non-indexer properties.  Emit the getter as a plain
                // method and the setter (if any) as Set{Name} so that ParameterizedPropertyRewriter
                // can rewrite call-site assignments to obj.SetFoo(k, v).
                bool renameItemGetter = hasAnyIndexer && string.Equals(group.Key, "Item", StringComparison.OrdinalIgnoreCase);
                string getName = MakeUniqueName(MakeSafeIdentifier(renameItemGetter ? "GetItem" : group.Key), usedMemberNames);
                var getParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters).ToArray();
                memberDecls.Add(MethodDeclaration(MemberType(propType, useDynamic), Identifier(getName))
                    .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                    .WithParameterList(ParameterList(SeparatedList(getParams)))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

                if (setter != null) {
                    var setParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters)
                        .Append(BuildSetterValueParam(propType, useDynamic, strictParameters))
                        .ToArray();
                    // Keep SetItem for pseudo-property rewriting when Item getter is renamed to GetItem.
                    string setName = renameItemGetter
                        ? MakeUniqueName("SetItem", usedMemberNames)
                        : "Set" + getName;
                    memberDecls.Add(MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(setName))
                        .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                        .WithParameterList(ParameterList(SeparatedList(setParams)))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                }
            }
            else {
                // Skip any "Item" property when an indexer is already being emitted — C# does not
                // allow both a named "Item" property and an indexer in the same type (CS0102).
                if (hasAnyIndexer && string.Equals(group.Key, "Item", StringComparison.OrdinalIgnoreCase))
                    continue;

                string propertyName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);
                var prop = PropertyDeclaration(MemberType(propType, useDynamic), Identifier(propertyName))
                    .WithAccessorList(AccessorList(List(accessors)));
                memberDecls.Add(prop);
            }
        }

        // If the interface inherits IEnumerable, skip re-declaring GetEnumerator — it is already
        // provided by the base interface, and re-declaring it causes CS0108 (shadows inherited member).
        bool inheritsIEnumerable = (type.ImplementedInterfaces ?? []).Any(i =>
            string.Equals(i, "IEnumerable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i, "System.Collections.IEnumerable", StringComparison.OrdinalIgnoreCase));

        var usedMethodSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in (type.Members ?? [])
                     .Where(m => m.Kind == LibraryMemberKind.Method)
                     .OrderBy(m => m.Name)) {
            // GetEnumerator is already declared by IEnumerable; re-declaring it in the derived
            // interface shadows the inherited member and produces CS0108.
            if (inheritsIEnumerable && string.Equals(method.Name, "GetEnumerator", StringComparison.OrdinalIgnoreCase)) continue;
            string paramSig = string.Join(",", method.Parameters.Select(p => p.Type));
            string rawMethodName = hasAnyIndexer && string.Equals(method.Name, "Item", StringComparison.OrdinalIgnoreCase)
                ? "GetItem"
                : method.Name;
            string methodName = MakeUniqueMethodName(MakeSafeIdentifier(rawMethodName), paramSig, usedMethodSignatures, usedMemberNames);
            var parameters = BuildParameters(method.Parameters, useDynamic, strictParameters: strictParameters).ToArray();

            var methodDecl = MethodDeclaration(
                    MemberType(method.ReturnType, useDynamic),
                    Identifier(methodName))
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            memberDecls.Add(methodDecl);
        }

        if (forwardingIndexer != null) memberDecls.Add(forwardingIndexer);

        var decl = InterfaceDeclaration(Identifier(emittedTypeName))
            .WithModifiers(Modifiers(isPublic: true))
            .WithMembers(List(memberDecls));

        string ifaceMarkerName = type.IsOleObject ? "IOleStub" : "IComStub";
        var ifaceMarkerBase = (BaseTypeSyntax)SimpleBaseType(IdentifierName(ifaceMarkerName));

        var baseInterfaces = (type.ImplementedInterfaces ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Where(i => i != "_Object")
            .Select(i => i.Contains('.') ? i : MakeSafeIdentifier(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => (BaseTypeSyntax)SimpleBaseType(ParseTypeName(n)))
            .ToArray();

        decl = decl.WithBaseList(BaseList(SeparatedList(
            new[] { ifaceMarkerBase }.Concat(baseInterfaces).ToArray())));

        return decl;
    }

    // Members already provided by System.Exception — re-declaring them in a derived stub
    // class causes CS0114 (hides inherited member) or CS0108 (shadows) errors.
    // COM exposes System.Exception via the dual interface _Exception.
    static readonly HashSet<string> ExceptionInheritedMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "HelpLink", "InnerException", "Message", "Source", "StackTrace", "TargetSite",
        "GetBaseException", "GetObjectData",
    };

    // Properties that are injected by the VB6 control extender
    static readonly (string Name, string CsType)[] VB6ControlExtenderProperties =
    [
        ("Left",            "int"),    ("Top",             "int"),
        ("Width",           "int"),    ("Height",          "int"),
        ("TabIndex",        "short"),  ("TabStop",         "bool"),
        ("Visible",         "bool"),   ("Enabled",         "bool"),
        ("Name",            "string"), ("Tag",             "string"),
        ("_ExtentX",        "int"),    ("_ExtentY",        "int"),
        ("_StockProps",     "int"),    ("_Version",        "int"),
        ("ToolTipText",     "string"), ("Index",           "int"),
        ("HelpContextID",   "int"),
        ("WhatsThisHelpID", "int"),
        ("DragMode",        "int"),
    ];

    static bool InheritsException(ComQueryType type) =>
        (type.ImplementedInterfaces ?? []).Any(i =>
            string.Equals(i, "_Exception", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i, "Exception", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i, "System.Exception", StringComparison.OrdinalIgnoreCase));

    // ──────────────────────────────────────────────────────────────────────
    // Class / module
    // ──────────────────────────────────────────────────────────────────────

    static ClassDeclarationSyntax GenerateClass(
        ComQueryLibrary library,
        ComQueryType type,
        string emittedTypeName,
        bool useDynamic = true,
        bool strictParameters = false)
    {
        bool isStatic = type.Kind == LibraryTypeKind.Module;
        bool exceptionDerived = !isStatic && InheritsException(type);

        var memberDecls = new List<MemberDeclarationSyntax>();
        // Seed with the class name to prevent CS0542 (member name same as enclosing type)
        var usedMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };

        // Group by name + kind to collapse get/set pairs into one property
        var propertyGroups = (type.Members ?? [])
            .Where(m => m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet)
            .GroupBy(m => m.Name)
            .ToList();

        var handledPropertyNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // Pre-compute whether a C# indexer will be emitted so that any same-named "Item"
        // property can be suppressed regardless of alphabetical ordering in the loop below.
        bool hasIndexer = propertyGroups.Any(g => {
            var pg = g.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
            return pg?.IsDefault == true && pg.Parameters.Count > 0;
        });
        var forwardingIndexer = !isStatic
            ? TryBuildDefaultForwardingIndexer(library, type, isForInterface: false, useDynamic, strictParameters)
            : null;
        bool hasAnyIndexer = hasIndexer || forwardingIndexer is not null;

        foreach (var group in propertyGroups.OrderBy(g => g.Key)) {
            if (exceptionDerived && ExceptionInheritedMembers.Contains(group.Key)) continue;
            var getter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
            var setter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertySet);

            string propType = getter != null ? getter.ReturnType : "object";
            if (propType == "void") propType = "object";

            var accessors = new List<AccessorDeclarationSyntax>();

            if (getter != null) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(ThrowNotImplementedExprBody())
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            if (setter != null) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithExpressionBody(ThrowNotImplementedExprBody())
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            // A default member (DISPID 0) with parameters becomes a C# indexer (this[...])
            // so that bang-operator conversions like rs!MyField → rs["MyField"] compile correctly.
            if (getter?.IsDefault == true && getter.Parameters.Count > 0) {
                var indexerParams = BuildParameters(getter.Parameters, useDynamic, skipFirstDefault: true, strictParameters: strictParameters).ToArray();
                var indexer = IndexerDeclaration(MemberType(propType, useDynamic))
                    .WithModifiers(Modifiers(isPublic: true, isStatic: false))
                    .WithParameterList(BracketedParameterList(SeparatedList(indexerParams)))
                    .WithAccessorList(AccessorList(List(accessors)));
                memberDecls.Add(indexer);

                // For non-"Item" default properties also emit a named method form so that VB6 code
                // that calls the property by name (e.g. xa.Value(i)) still compiles after
                // conversion via ParameterizedPropertyRewriter.  "Item"-named properties are
                // excluded because the method loop already handles any explicit Item method by
                // renaming it to GetItem — adding another GetItem here would produce a duplicate.
                if (!string.Equals(group.Key, "Item", StringComparison.OrdinalIgnoreCase)) {
                    string getName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);
                    var getParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters).ToArray();
                    memberDecls.Add(MethodDeclaration(MemberType(propType, useDynamic), Identifier(getName))
                        .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                        .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                        .WithParameterList(ParameterList(SeparatedList(getParams)))
                        .WithExpressionBody(ThrowNotImplementedExprBody())
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

                    if (setter != null) {
                        string setName = MakeUniqueName("Set" + MakeSafeIdentifier(group.Key), usedMemberNames);
                        var setParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters)
                            .Append(BuildSetterValueParam(propType, useDynamic, strictParameters))
                            .ToArray();
                        memberDecls.Add(MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(setName))
                            .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                            .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                            .WithParameterList(ParameterList(SeparatedList(setParams)))
                            .WithExpressionBody(ThrowNotImplementedExprBody())
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                    }
                }
            }
            else if (getter?.Parameters.Count > 0) {
                // C# has no parameterized non-indexer properties.  Emit the getter as a plain
                // method and the setter (if any) as Set{Name} so that ParameterizedPropertyRewriter
                // can rewrite call-site assignments to obj.SetFoo(k, v).
                bool renameItemGetter = hasAnyIndexer && string.Equals(group.Key, "Item", StringComparison.OrdinalIgnoreCase);
                string getName = MakeUniqueName(MakeSafeIdentifier(renameItemGetter ? "GetItem" : group.Key), usedMemberNames);
                var getParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters).ToArray();
                memberDecls.Add(MethodDeclaration(MemberType(propType, useDynamic), Identifier(getName))
                    .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                    .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                    .WithParameterList(ParameterList(SeparatedList(getParams)))
                    .WithExpressionBody(ThrowNotImplementedExprBody())
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

                if (setter != null) {
                    var setParams = BuildParameters(getter.Parameters, useDynamic, strictParameters: strictParameters)
                        .Append(BuildSetterValueParam(propType, useDynamic, strictParameters))
                        .ToArray();
                    // Keep SetItem for pseudo-property rewriting when Item getter is renamed to GetItem.
                    string setName = renameItemGetter
                        ? MakeUniqueName("SetItem", usedMemberNames)
                        : "Set" + getName;
                    memberDecls.Add(MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier(setName))
                        .WithAttributeLists(SingletonList(IndexedPropertyAttributeList(group.Key)))
                        .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                        .WithParameterList(ParameterList(SeparatedList(setParams)))
                        .WithExpressionBody(ThrowNotImplementedExprBody())
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
                }
            }
            else {
                // Skip any "Item" property when an indexer is already being emitted — C# does not
                // allow both a named "Item" property and an indexer in the same class (CS0102).
                if (hasAnyIndexer && string.Equals(group.Key, "Item", StringComparison.OrdinalIgnoreCase)) {
                    handledPropertyNames.Add(group.Key);
                    continue;
                }

                string propertyName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);
                var prop = PropertyDeclaration(MemberType(propType, useDynamic), Identifier(propertyName))
                    .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                    .WithAccessorList(AccessorList(List(accessors)));
                memberDecls.Add(prop);
            }

            handledPropertyNames.Add(group.Key);
        }

        // Methods (skip anything already emitted as property)
        var usedMethodSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in (type.Members ?? [])
                     .Where(m => m.Kind == LibraryMemberKind.Method)
                     .OrderBy(m => m.Name)) {
            if (exceptionDerived && ExceptionInheritedMembers.Contains(method.Name)) continue;
            string paramSig = string.Join(",", method.Parameters.Select(p => p.Type));
            string rawMethodName = hasAnyIndexer && string.Equals(method.Name, "Item", StringComparison.OrdinalIgnoreCase)
                ? "GetItem"
                : method.Name;
            string methodName = MakeUniqueMethodName(MakeSafeIdentifier(rawMethodName), paramSig, usedMethodSignatures, usedMemberNames);

            var parameters = BuildParameters(method.Parameters, useDynamic, strictParameters: strictParameters).ToArray();

            var methodDecl = MethodDeclaration(
                    MemberType(method.ReturnType, useDynamic),
                    Identifier(methodName))
                .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithExpressionBody(ThrowNotImplementedExprBody())
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            memberDecls.Add(methodDecl);
        }

        if (forwardingIndexer != null) memberDecls.Add(forwardingIndexer);

        if (type.IsControl && !isStatic) {
            foreach (var (extName, extCsType) in VB6ControlExtenderProperties) {
                if (handledPropertyNames.Contains(extName)) continue;
                string safeExtName = MakeSafeIdentifier(extName);
                usedMemberNames.Add(safeExtName);
                var extProp = PropertyDeclaration(ParseTypeName(extCsType), Identifier(safeExtName))
                    .WithModifiers(Modifiers(isPublic: true, isStatic: false))
                    .WithAccessorList(AccessorList(List(new[] {
                        AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithExpressionBody(ThrowNotImplementedExprBody())
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                        AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithExpressionBody(ThrowNotImplementedExprBody())
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    })));
                memberDecls.Add(extProp);
            }
        }

        var decl = ClassDeclaration(Identifier(emittedTypeName))
            .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
            .WithMembers(List(memberDecls));

        if (!isStatic) {
            string markerName = type.IsOleObject ? "IOleStub" : "IComStub";
            var markerBase = (BaseTypeSyntax)SimpleBaseType(IdentifierName(markerName));

            var baseInterfaces = (type.ImplementedInterfaces ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Where(i => i != "_Object")  // _Object is a COM plumbing artefact; omit it
                .Select(i => i.Contains('.') ? i : MakeSafeIdentifier(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => (BaseTypeSyntax)SimpleBaseType(ParseTypeName(n)))
                .ToArray();

            // ActiveX control stubs also implement IControlStub<TSelf> to expose the VB6
            // extender's Object property (returns the underlying control as its own type).
            BaseTypeSyntax[] controlBase = type.IsControl
                ? [(BaseTypeSyntax)SimpleBaseType(
                    GenericName(Identifier("IControlStub"))
                        .WithTypeArgumentList(TypeArgumentList(
                            SingletonSeparatedList<TypeSyntax>(
                                IdentifierName(emittedTypeName)))))]
                : [];

            decl = decl.WithBaseList(BaseList(SeparatedList(
                new[] { markerBase }.Concat(controlBase).Concat(baseInterfaces).ToArray())));
        }

        return decl;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Default-member forwarding indexer
    // ──────────────────────────────────────────────────────────────────────

    static string GenerateExtenderExtensionSource(
        ComQueryLibrary library,
        string emittedTypeName,
        string classSource)
    {
        var classTree = CSharpSyntaxTree.ParseText(classSource);
        var classRoot = classTree.GetCompilationUnitRoot();

        var classDecl = classRoot.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => string.Equals(c.Identifier.ValueText, emittedTypeName, StringComparison.Ordinal));
        if (classDecl == null) {
            throw new InvalidOperationException($"Failed to locate generated class '{emittedTypeName}' while building extender extension block.");
        }

        var extensionMembers = new List<MemberDeclarationSyntax>();
        foreach (var member in classDecl.Members) {
            switch (member) {
                case MethodDeclarationSyntax methodDecl:
                    extensionMembers.Add(BuildForwardingMethod(methodDecl, emittedTypeName));
                    break;

                case PropertyDeclarationSyntax propertyDecl:
                    if (string.Equals(propertyDecl.Identifier.ValueText, "Object", StringComparison.Ordinal))
                        continue;
                    extensionMembers.Add(BuildForwardingProperty(propertyDecl, emittedTypeName));
                    break;

                case IndexerDeclarationSyntax indexerDecl:
                    extensionMembers.Add(BuildForwardingIndexer(indexerDecl, emittedTypeName));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported member kind '{member.Kind()}' in generated class '{emittedTypeName}' while emitting extender extension block.");
            }
        }

        if (extensionMembers.Count == 0) {
            throw new InvalidOperationException(
                $"No members available to emit for extender extension block of '{emittedTypeName}'.");
        }

        var extensionReceiverParam = Parameter(Identifier("self"))
            .WithType(IdentifierName("IComStub"));

        var extensionBlock = ExtensionBlockDeclaration(
            default,
            default,
            Token(SyntaxKind.ExtensionKeyword),
            default,
            ParameterList(SingletonSeparatedList(extensionReceiverParam)),
            default,
            Token(SyntaxKind.OpenBraceToken),
            List(extensionMembers),
            Token(SyntaxKind.CloseBraceToken),
            default);

        var extensionClass = ClassDeclaration(Identifier($"{emittedTypeName}Extensions"))
            .WithModifiers(Modifiers(isPublic: true, isStatic: true))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(extensionBlock));

        var ns = IdentifierName(library.SafeName);
        var extensionCu = CompilationUnit(
                default,
                default,
                default,
                SingletonList<MemberDeclarationSyntax>(
                    FileScopedNamespaceDeclaration(ns)
                        .WithMembers(SingletonList<MemberDeclarationSyntax>(extensionClass))))
            .NormalizeWhitespace();

        string extensionSource = extensionCu.ToFullString();

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var extensionTree = CSharpSyntaxTree.ParseText(extensionSource, parseOptions);
        var parseErrors = extensionTree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .Select(d => d.ToString())
            .ToArray();

        if (parseErrors.Length > 0) {
            throw new InvalidOperationException(
                $"Failed to emit extension block for '{emittedTypeName}'. Parse errors: {string.Join(" | ", parseErrors)}");
        }

        return extensionSource;
    }

    static MethodDeclarationSyntax BuildForwardingMethod(MethodDeclarationSyntax methodDecl, string emittedTypeName)
    {
        var callArgs = methodDecl.ParameterList.Parameters
            .Select(BuildForwardingArgument)
            .ToArray();

        var callExpr = InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                BuildForwardingReceiver(emittedTypeName),
                IdentifierName(methodDecl.Identifier)),
            ArgumentList(SeparatedList(callArgs)));

        return MethodDeclaration(methodDecl.ReturnType, methodDecl.Identifier)
            .WithModifiers(Modifiers(isPublic: true))
            .WithParameterList(methodDecl.ParameterList)
            .WithExpressionBody(ArrowExpressionClause(callExpr))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    static PropertyDeclarationSyntax BuildForwardingProperty(PropertyDeclarationSyntax propertyDecl, string emittedTypeName)
    {
        var accessors = new List<AccessorDeclarationSyntax>();

        foreach (var accessor in propertyDecl.AccessorList?.Accessors ?? []) {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(ArrowExpressionClause(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                BuildForwardingReceiver(emittedTypeName),
                                IdentifierName(propertyDecl.Identifier))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithExpressionBody(ArrowExpressionClause(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    BuildForwardingReceiver(emittedTypeName),
                                    IdentifierName(propertyDecl.Identifier)),
                                IdentifierName("value"))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }
        }

        return PropertyDeclaration(propertyDecl.Type, propertyDecl.Identifier)
            .WithModifiers(Modifiers(isPublic: true))
            .WithAccessorList(AccessorList(List(accessors)));
    }

    static IndexerDeclarationSyntax BuildForwardingIndexer(IndexerDeclarationSyntax indexerDecl, string emittedTypeName)
    {
        var elementAccess = ElementAccessExpression(
            BuildForwardingReceiver(emittedTypeName),
            BracketedArgumentList(SeparatedList(
                indexerDecl.ParameterList.Parameters.Select(BuildForwardingArgument))));

        var accessors = new List<AccessorDeclarationSyntax>();
        foreach (var accessor in indexerDecl.AccessorList?.Accessors ?? []) {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(ArrowExpressionClause(elementAccess))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }

            if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithExpressionBody(ArrowExpressionClause(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                elementAccess,
                                IdentifierName("value"))))
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }
        }

        return IndexerDeclaration(indexerDecl.Type)
            .WithModifiers(Modifiers(isPublic: true))
            .WithParameterList(indexerDecl.ParameterList)
            .WithAccessorList(AccessorList(List(accessors)));
    }

    static ArgumentSyntax BuildForwardingArgument(ParameterSyntax parameter)
    {
        var argument = Argument(IdentifierName(parameter.Identifier));
        if (parameter.Type is RefTypeSyntax) {
            argument = argument.WithRefKindKeyword(Token(SyntaxKind.RefKeyword));
        }

        return argument;
    }

    static ExpressionSyntax BuildForwardingReceiver(string emittedTypeName)
    {
        return ParenthesizedExpression(
            CastExpression(
                IdentifierName(emittedTypeName),
                IdentifierName("self")));
    }

    /// <summary>
    /// When a type's DISPID 0 property has <em>no</em> parameters (e.g. <c>Recordset.Fields</c>
    /// returns <c>DAO.Fields</c>), VB6's bang operator chains through two default-member
    /// lookups: <c>rs!MyField</c> → <c>rs.Fields("MyField")</c>. The VB6Converter emits
    /// this as <c>rs["MyField"]</c>, so the outer type must expose a <c>this[]</c> indexer.
    /// <para>
    /// This method detects that pattern and emits a forwarding <c>this[]</c> whose
    /// parameter signature matches the inner collection's own parameterised DISPID 0
    /// member, so the converted code compiles without requiring semantic rewrites.
    /// </para>
    /// </summary>
    static MemberDeclarationSyntax? TryBuildDefaultForwardingIndexer(
        ComQueryLibrary library,
        ComQueryType type,
        bool isForInterface,
        bool useDynamic = true,
        bool strictParameters = false)
    {
        // Find DISPID 0 PropertyGet with NO parameters (e.g. Fields on Recordset).
        var noParamDefault = (type.Members ?? []).FirstOrDefault(
            m => m.Kind == LibraryMemberKind.PropertyGet && m.IsDefault && m.Parameters.Count == 0);
        if (noParamDefault == null) return null;

        // Resolve the return type to a type in the same library.
        string returnTypeName = noParamDefault.ReturnType;
        string simpleTypeName = returnTypeName.Contains('.')
            ? returnTypeName[(returnTypeName.LastIndexOf('.') + 1)..]
            : returnTypeName;

        var innerType = (library.Types ?? []).FirstOrDefault(t =>
            string.Equals(t.Name, simpleTypeName, StringComparison.OrdinalIgnoreCase));
        if (innerType == null) return null;

        // Check whether the inner type itself has a parameterised default member
        // (i.e. it is a collection with this[]).
        var innerIndexer = (innerType.Members ?? []).FirstOrDefault(
            m => m.Kind == LibraryMemberKind.PropertyGet && m.IsDefault && m.Parameters.Count > 0);
        if (innerIndexer == null) return null;

        string propName  = MakeSafeIdentifier(noParamDefault.Name);
        var indexerParams   = BuildParameters(innerIndexer.Parameters, useDynamic, skipFirstDefault: true, strictParameters: strictParameters).ToArray();

        // Recursively follow the no-param default property chain on the item type.
        // e.g. Fields["key"] returns Field, and Field.Value (no-param DISPID 0) returns object,
        // so the outer indexer on Recordset should return object (dynamic), not Field.
        string rawReturnType = innerIndexer.ReturnType;
        string returnCsType  = ResolveDefaultChainType(rawReturnType, library);

        // Determine whether the forwarding indexer should expose a setter.
        // If we followed the chain one more step (e.g. Field → Field.Value), check for a
        // no-param DISPID 0 PropertySet on the intermediate item type (Field).
        // Otherwise fall back to looking for a parameterised DISPID 0 PropertySet on innerType.
        bool hasSetter = returnCsType != rawReturnType
            ? HasNoParamDefaultSetter(rawReturnType, library)
            : (innerType.Members ?? []).Any(
                  m => m.Kind == LibraryMemberKind.PropertySet && m.IsDefault && m.Parameters.Count > 0);

        var accessors = new List<AccessorDeclarationSyntax>();
        if (isForInterface) {
            accessors.Add(
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            if (hasSetter) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }
        }
        else {
            accessors.Add(
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithExpressionBody(ThrowNotImplementedExprBody())
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            if (hasSetter) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithExpressionBody(ThrowNotImplementedExprBody())
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
            }
        }

        var indexer = IndexerDeclaration(MemberType(returnCsType, useDynamic))
            .WithParameterList(BracketedParameterList(SeparatedList(indexerParams)))
            .WithAccessorList(AccessorList(List(accessors)));

        if (!isForInterface) {
            indexer = indexer.WithModifiers(Modifiers(isPublic: true, isStatic: false));
        }

        return indexer;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Struct (TKIND_RECORD / TKIND_UNION)
    // ──────────────────────────────────────────────────────────────────────

    static StructDeclarationSyntax GenerateStruct(
        ComQueryType type,
        string emittedTypeName,
        HashSet<(string TypeName, string FieldName)> cyclicFields,
        bool useDynamic = true)
    {
        // Seed with the struct name to prevent CS0542 (member name same as enclosing type)
        var usedFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };
        var fieldDecls = new List<MemberDeclarationSyntax>();

        foreach (var field in (type.Members ?? []).Where(m => m.Kind == LibraryMemberKind.Field)) {
            string fieldName = MakeUniqueName(MakeSafeIdentifier(field.Name), usedFieldNames);

            if (cyclicFields.Contains((emittedTypeName, field.Name))) {
                // The COM type library records this as a value-type field, but the original
                // C definition uses a pointer (e.g. TYPEDESC*).  The type library strips the
                // pointer indirection, which would create a struct layout cycle in C#.
                // Emit nint (pointer-sized integer) so the field name is preserved and the
                // struct compiles.  See docs/com.md for details.
                fieldDecls.Add(
                    FieldDeclaration(
                        VariableDeclaration(ParseTypeName("nint"))
                            .WithVariables(SingletonSeparatedList(
                                VariableDeclarator(Identifier(fieldName)))))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .WithLeadingTrivia(
                        Comment($"// was: {field.ReturnType} — COM pointer field; replaced with nint to avoid struct layout cycle")));
                continue;
            }

            fieldDecls.Add(
                FieldDeclaration(
                    VariableDeclaration(MemberType(field.ReturnType, useDynamic))
                        .WithVariables(SingletonSeparatedList(
                            VariableDeclarator(Identifier(fieldName)))))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword))));
        }

        return StructDeclaration(Identifier(emittedTypeName))
            .WithModifiers(Modifiers(isPublic: true))
            .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(
                SimpleBaseType(IdentifierName("IComStub")))))
            .WithMembers(List(fieldDecls));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Struct cycle detection
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the set of (emittedTypeName, originalFieldName) pairs whose field type
    /// forms a back-edge in the struct dependency graph of <paramref name="library"/>.
    /// Every identified field is a COM pointer that the type library exposed as a
    /// by-value member, causing an illegal recursive struct layout in C#.
    /// </summary>
    static HashSet<(string TypeName, string FieldName)> DetectStructCycles(
        ComQueryLibrary library,
        Dictionary<string, string> structQualifiedToEmitted)
    {
        // Build adjacency list: emittedTypeName → [(fieldName, emittedTargetTypeName)]
        // Only edges that point to other structs within the same library are included.
        var deps = new Dictionary<string, List<(string FieldName, string Target)>>(StringComparer.Ordinal);

        foreach (var type in (library.Types ?? []).Where(t => t.Kind == LibraryTypeKind.Struct)) {
            string qualName = $"{library.SafeName}.{type.Name}";
            if (!structQualifiedToEmitted.TryGetValue(qualName, out string? emitted)) continue;

            var edges = new List<(string, string)>();
            foreach (var field in (type.Members ?? []).Where(m => m.Kind == LibraryMemberKind.Field)) {
                if (structQualifiedToEmitted.TryGetValue(field.ReturnType, out string? targetEmitted))
                    edges.Add((field.Name, targetEmitted));
            }
            deps[emitted] = edges;
        }

        // DFS with tri-colour marking: 0 = unvisited, 1 = in stack (gray), 2 = done (black).
        // A gray → gray edge is a back-edge and identifies a cyclic field.
        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        var cyclicFields = new HashSet<(string, string)>();

        foreach (var node in deps.Keys)
            if (!color.ContainsKey(node))
                DfsStructCycle(node, deps, color, cyclicFields);

        return cyclicFields;
    }

    static void DfsStructCycle(
        string node,
        Dictionary<string, List<(string FieldName, string Target)>> deps,
        Dictionary<string, int> color,
        HashSet<(string TypeName, string FieldName)> cyclicFields)
    {
        color[node] = 1; // gray — on current DFS path

        if (deps.TryGetValue(node, out var edges)) {
            foreach (var (fieldName, target) in edges) {
                color.TryGetValue(target, out int targetColor);
                if (targetColor == 1) {
                    // Back-edge: this field's type is an ancestor on the current path.
                    cyclicFields.Add((node, fieldName));
                } else if (targetColor == 0) {
                    DfsStructCycle(target, deps, color, cyclicFields);
                }
                // targetColor == 2 (black): already fully explored, no cycle via this edge.
            }
        }

        color[node] = 2; // black — fully explored
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────────
    // Default-property chain resolution helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Follows the chain of no-parameter default properties (DISPID 0) starting from
    /// <paramref name="typeName"/> until no further no-param default exists, and returns
    /// the terminal return type.  This mirrors VB6's implicit default-member resolution:
    /// <c>rs!SomeColumn</c> → <c>rs.Fields["SomeColumn"].Value</c>, where <c>.Value</c>
    /// is the no-param default property of <c>Field</c>.
    /// <para>A visited-set guards against cycles in pathological type libraries.</para>
    /// </summary>
    static string ResolveDefaultChainType(string typeName, ComQueryLibrary library)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = typeName;

        while (visited.Add(current)) {
            string simple = current.Contains('.')
                ? current[(current.LastIndexOf('.') + 1)..]
                : current;

            var resolvedType = (library.Types ?? []).FirstOrDefault(t =>
                string.Equals(t.Name, simple, StringComparison.OrdinalIgnoreCase));
            if (resolvedType == null) break;

            var noParamDefault = (resolvedType.Members ?? []).FirstOrDefault(
                m => m.Kind == LibraryMemberKind.PropertyGet && m.IsDefault && m.Parameters.Count == 0);
            if (noParamDefault == null) break;

            current = noParamDefault.ReturnType;
        }

        return current;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the type identified by <paramref name="typeName"/>
    /// exposes a writable no-index-parameter default property (DISPID 0 PropertySet).
    /// <para>
    /// A plain property setter (e.g. <c>Field.Value = x</c>) has exactly one parameter in
    /// the COM type library — the value to assign — which is never an index.  We therefore
    /// accept setters with zero or one parameters; setters with two or more parameters carry
    /// at least one index parameter and belong to parameterised indexers.
    /// </para>
    /// </summary>
    static bool HasNoParamDefaultSetter(string typeName, ComQueryLibrary library)
    {
        string simple = typeName.Contains('.')
            ? typeName[(typeName.LastIndexOf('.') + 1)..]
            : typeName;

        var resolvedType = (library.Types ?? []).FirstOrDefault(t =>
            string.Equals(t.Name, simple, StringComparison.OrdinalIgnoreCase));
        if (resolvedType == null) return false;

        // Count <= 1: plain setters expose at most the one value-to-assign parameter;
        // they do not carry any additional index parameters.
        return (resolvedType.Members ?? []).Any(
            m => m.Kind == LibraryMemberKind.PropertySet && m.IsDefault && m.Parameters.Count <= 1);
    }

    /// <summary>Maps a COM member type string to a Roslyn TypeSyntax, substituting
    /// <c>dynamic</c> for <c>object</c> when <paramref name="useDynamic"/> is true.</summary>
    static TypeSyntax MemberType(string typeName, bool useDynamic) =>
        useDynamic && string.Equals(typeName, "object", StringComparison.OrdinalIgnoreCase)
            ? (TypeSyntax)IdentifierName("dynamic")
            : ParseTypeName(typeName);

    // C# requires that once a parameter has a default value, all subsequent parameters must
    // also have defaults.  Walk the list and force-optionalize any required `ref` param that
    // follows an optional one.  See docs/com.md for the known caveats.
    static IEnumerable<ParameterSyntax> BuildParameters(
        IReadOnlyList<ComQueryParam> ps,
        bool useDynamic = true,
        bool stripDefaults = false,
        bool skipFirstDefault = false,
        bool strictParameters = false)
    {
        bool seenOptional = false;
        for (int i = 0; i < ps.Count; i++) {
            var p = ps[i];
            bool stripDefaultForCurrent = stripDefaults || (skipFirstDefault && i == 0);

            if (p.IsOptional && !stripDefaultForCurrent) {
                seenOptional = true;
            }

            yield return BuildParameter(
                p,
                useDynamic,
                forceOptional: !stripDefaultForCurrent && seenOptional && !p.IsOptional,
                stripDefaults: stripDefaultForCurrent,
                strictParameters: strictParameters);
        }
    }

    static ParameterSyntax BuildSetterValueParam(string type, bool useDynamic, bool strictParameters)
    {
        var p = Parameter(Identifier("value")).WithType(MemberType(type, useDynamic));
        if (!strictParameters)
            p = p.WithDefault(EqualsValueClause(LiteralExpression(SyntaxKind.DefaultLiteralExpression, Token(SyntaxKind.DefaultKeyword))));
        return p;
    }

    static ParameterSyntax BuildParameter(
        ComQueryParam p,
        bool useDynamic = true,
        bool forceOptional = false,
        bool stripDefaults = false,
        bool strictParameters = false)
    {
        // C# does not allow `ref` parameters to have default values, and does not allow
        // required parameters to follow optional ones.  In both cases we drop `ref` and
        // make the parameter optional.  See docs/com.md for the known caveats.
        // Indexers suppress defaults only for their first parameter to avoid over-optional
        // leading arguments while preserving trailing COM optional defaults.
        // `params` parameters cannot have defaults, so IsParamArray suppresses makeOptional too.
        bool makeOptional = !stripDefaults
            && !p.IsParamArray
            && (!strictParameters || p.IsOptional || forceOptional);
        bool useRef = p.IsOut && !makeOptional;
        var syntax = Parameter(Identifier(MakeSafeIdentifier(p.Name)))
        .WithType(useRef ? ParseTypeName("ref " + p.Type) : MemberType(p.Type, useDynamic));
        if (p.IsParamArray) {
            syntax = syntax.WithModifiers(TokenList(Token(SyntaxKind.ParamsKeyword)));
        }
        if (makeOptional) {
            syntax = syntax.WithDefault(
                EqualsValueClause(
                    LiteralExpression(SyntaxKind.DefaultLiteralExpression,
                        Token(SyntaxKind.DefaultKeyword))));
        }

        return syntax;
    }

    static ArrowExpressionClauseSyntax ThrowNotImplementedExprBody() =>
        ArrowExpressionClause(
            ThrowExpression(
                ObjectCreationExpression(
                    QualifiedName(IdentifierName("System"), IdentifierName("NotImplementedException")))
                    .WithArgumentList(ArgumentList())));

    static readonly HashSet<string> _csKeywords = new(System.StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked",
        "class","const","continue","decimal","default","delegate","do","double","else",
        "enum","event","explicit","extern","false","finally","fixed","float","for",
        "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
        "long","namespace","new","null","object","operator","out","override","params",
        "private","protected","public","readonly","ref","return","sbyte","sealed",
        "short","sizeof","stackalloc","static","string","struct","switch","this",
        "throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
        "using","virtual","void","volatile","while",
    };

    public static string MakeSafeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_";
        // Prefix reserved keywords with @
        if (_csKeywords.Contains(name)) return "@" + name;
        // Replace any character that's invalid in an identifier
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name) {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        if (!char.IsLetter(sb[0]) && sb[0] != '_') sb.Insert(0, '_');
        return sb.ToString();
    }

    static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        if (string.IsNullOrWhiteSpace(baseName)) {
            baseName = "_";
        }

        if (usedNames.Add(baseName)) {
            return baseName;
        }

        int suffix = 2;
        while (true) {
            string candidate = $"{baseName}_{suffix}";
            if (usedNames.Add(candidate)) {
                return candidate;
            }
            suffix++;
        }
    }

    /// <summary>
    /// Resolves a unique method name that allows overloading: two methods with the same
    /// <paramref name="baseName"/> but different <paramref name="paramSignature"/> strings
    /// are treated as C# overloads and both return <paramref name="baseName"/> unchanged.
    /// Only a true duplicate (same name AND same parameter types) triggers the _2 suffix.
    /// </summary>
    static string MakeUniqueMethodName(
        string baseName,
        string paramSignature,
        HashSet<string> usedSignatures,
        HashSet<string> forbiddenNames)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "_";

        string sig = $"{baseName}({paramSignature})";
        if (!forbiddenNames.Contains(baseName) && usedSignatures.Add(sig)) {
            return baseName;
        }

        int suffix = 2;
        while (true) {
            string candidate = $"{baseName}_{suffix}";
            string candidateSig = $"{candidate}({paramSignature})";
            if (!forbiddenNames.Contains(candidate) && usedSignatures.Add(candidateSig)) {
                return candidate;
            }
            suffix++;
        }
    }

}
