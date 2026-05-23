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
    public static IReadOnlyList<string> Generate(ComQueryLibrary library, string referenceRoot, bool filterComPlumbing = true)
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
            string? source = GenerateType(library, type, emittedTypeName, cyclicFields, filterComPlumbing);
            if (source == null) continue;

            string filePath = Path.Combine(libDir, $"{emittedTypeName}.cs");
            File.WriteAllText(filePath, source);
            written.Add(filePath);
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

    // ──────────────────────────────────────────────────────────────────────
    // Per-type dispatch
    // ──────────────────────────────────────────────────────────────────────

    static string? GenerateType(
        ComQueryLibrary library,
        ComQueryType type,
        string emittedTypeName,
        HashSet<(string TypeName, string FieldName)> cyclicFields,
        bool filterComPlumbing)
    {
        MemberDeclarationSyntax? decl = type.Kind switch {
            LibraryTypeKind.Enum                                              => GenerateEnum(type, emittedTypeName),
            LibraryTypeKind.DispatchInterface or LibraryTypeKind.Interface     => GenerateInterface(library, type, emittedTypeName),
            LibraryTypeKind.Class or LibraryTypeKind.Module                    => GenerateClass(library, type, emittedTypeName),
            LibraryTypeKind.Struct                                            => GenerateStruct(type, emittedTypeName, cyclicFields),
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
            .WithGeneratedCodeAttribute()
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

    static InterfaceDeclarationSyntax GenerateInterface(ComQueryLibrary library, ComQueryType type, string emittedTypeName)
    {
        var memberDecls = new List<MemberDeclarationSyntax>();
        // Seed with the interface name to prevent CS0542 (member name same as enclosing type)
        var usedMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };

        var propertyGroups = (type.Members ?? [])
            .Where(m => m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet)
            .GroupBy(m => m.Name)
            .ToList();

        bool hasIndexer = false;
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
                var indexerParams = BuildParameters(getter.Parameters).ToArray();
                var indexer = IndexerDeclaration(ParseTypeName(propType))
                    .WithParameterList(BracketedParameterList(SeparatedList(indexerParams)))
                    .WithAccessorList(AccessorList(List(accessors)));
                memberDecls.Add(indexer);
                hasIndexer = true;
            }
            else {
                string propertyName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);
                var prop = PropertyDeclaration(ParseTypeName(propType), Identifier(propertyName))
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
            // In C#, "Item" is reserved as the indexer accessor name; skip any duplicate Method
            // named Item when an indexer has already been emitted to avoid CS0102.
            if (hasIndexer && string.Equals(method.Name, "Item", StringComparison.OrdinalIgnoreCase)) continue;
            // GetEnumerator is already declared by IEnumerable; re-declaring it in the derived
            // interface shadows the inherited member and produces CS0108.
            if (inheritsIEnumerable && string.Equals(method.Name, "GetEnumerator", StringComparison.OrdinalIgnoreCase)) continue;
            string paramSig = string.Join(",", method.Parameters.Select(p => p.Type));
            string methodName = MakeUniqueMethodName(MakeSafeIdentifier(method.Name), paramSig, usedMethodSignatures, usedMemberNames);
            var parameters = BuildParameters(method.Parameters).ToArray();

            var methodDecl = MethodDeclaration(
                    ParseTypeName(method.ReturnType),
                    Identifier(methodName))
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            memberDecls.Add(methodDecl);
        }

        var forwardingIndexer = TryBuildDefaultForwardingIndexer(library, type, isForInterface: true);
        if (forwardingIndexer != null) memberDecls.Add(forwardingIndexer);

        var decl = InterfaceDeclaration(Identifier(emittedTypeName))
            .WithModifiers(Modifiers(isPublic: true))
            .WithGeneratedCodeAttribute()
            .WithMembers(List(memberDecls));

        var baseInterfaces = (type.ImplementedInterfaces ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Where(i => i != "_Object")
            .Select(i => i.Contains('.') ? i : MakeSafeIdentifier(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => (BaseTypeSyntax)SimpleBaseType(ParseTypeName(n)))
            .ToArray();

        if (baseInterfaces.Length > 0) {
            decl = decl.WithBaseList(BaseList(SeparatedList(baseInterfaces)));
        }

        return decl;
    }

    // Members already provided by System.Exception — re-declaring them in a derived stub
    // class causes CS0114 (hides inherited member) or CS0108 (shadows) errors.
    static readonly HashSet<string> ExceptionInheritedMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "HelpLink", "InnerException", "Message", "Source", "StackTrace", "TargetSite",
        "GetBaseException", "GetObjectData",
    };

    // COM exposes System.Exception via the dual interface _Exception.
    static readonly (string Name, string CsType)[] VB6ControlExtenderProperties =
    [
        ("Left",            "int"),    ("Top",             "int"),
        ("Width",           "int"),    ("Height",          "int"),
        ("TabIndex",        "short"),  ("TabStop",         "bool"),
        ("Visible",         "bool"),   ("Enabled",         "bool"),
        ("Name",            "string"), ("Tag",             "string"),
        ("_ExtentX",        "int"),    ("_ExtentY",        "int"),
        ("_StockProps",     "int"),
        ("ToolTipText",     "string"),
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

    static ClassDeclarationSyntax GenerateClass(ComQueryLibrary library, ComQueryType type, string emittedTypeName)
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
        bool hasIndexer = false;

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
                var indexerParams = BuildParameters(getter.Parameters).ToArray();
                var indexer = IndexerDeclaration(ParseTypeName(propType))
                    .WithModifiers(Modifiers(isPublic: true, isStatic: false))
                    .WithParameterList(BracketedParameterList(SeparatedList(indexerParams)))
                    .WithAccessorList(AccessorList(List(accessors)));
                memberDecls.Add(indexer);
                hasIndexer = true;
            }
            else {
                string propertyName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);
                var prop = PropertyDeclaration(ParseTypeName(propType), Identifier(propertyName))
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
            // In C#, "Item" is reserved as the indexer accessor name; skip any duplicate Method
            // named Item when an indexer has already been emitted to avoid CS0102.
            if (hasIndexer && string.Equals(method.Name, "Item", StringComparison.OrdinalIgnoreCase)) continue;
            string paramSig = string.Join(",", method.Parameters.Select(p => p.Type));
            string methodName = MakeUniqueMethodName(MakeSafeIdentifier(method.Name), paramSig, usedMethodSignatures, usedMemberNames);

            var parameters = BuildParameters(method.Parameters).ToArray();

            var methodDecl = MethodDeclaration(
                    ParseTypeName(method.ReturnType),
                    Identifier(methodName))
                .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithExpressionBody(ThrowNotImplementedExprBody())
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            memberDecls.Add(methodDecl);
        }

        if (!isStatic) {
            var forwardingIndexer = TryBuildDefaultForwardingIndexer(library, type, isForInterface: false);
            if (forwardingIndexer != null) memberDecls.Add(forwardingIndexer);
        }

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
            .WithGeneratedCodeAttribute()
            .WithMembers(List(memberDecls));

        if (!isStatic) {
            var baseInterfaces = (type.ImplementedInterfaces ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Where(i => i != "_Object")  // _Object is a COM plumbing artefact; omit it
                .Select(i => i.Contains('.') ? i : MakeSafeIdentifier(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => (BaseTypeSyntax)SimpleBaseType(ParseTypeName(n)))
                .ToArray();

            if (baseInterfaces.Length > 0) {
                decl = decl.WithBaseList(BaseList(SeparatedList(baseInterfaces)));
            }
        }

        return decl;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Default-member forwarding indexer
    // ──────────────────────────────────────────────────────────────────────

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
        bool isForInterface)
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
        var indexerParams   = BuildParameters(innerIndexer.Parameters).ToArray();

        // Recursively follow the no-param default property chain on the item type.
        // e.g. Fields["key"] returns Field, and Field.Value (no-param DISPID 0) returns object,
        // so the outer indexer on Recordset should return object, not Field.
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

        var indexer = IndexerDeclaration(ParseTypeName(returnCsType))
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
        HashSet<(string TypeName, string FieldName)> cyclicFields)
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
                    VariableDeclaration(ParseTypeName(field.ReturnType))
                        .WithVariables(SingletonSeparatedList(
                            VariableDeclarator(Identifier(fieldName)))))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword))));
        }

        return StructDeclaration(Identifier(emittedTypeName))
            .WithModifiers(Modifiers(isPublic: true))
            .WithGeneratedCodeAttribute()
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

    // C# requires that once a parameter has a default value, all subsequent parameters must
    // also have defaults.  Walk the list and force-optionalize any required `ref` param that
    // follows an optional one.  See docs/com.md for the known caveats.
    static IEnumerable<ParameterSyntax> BuildParameters(IReadOnlyList<ComQueryParam> ps)
    {
        bool seenOptional = false;
        foreach (var p in ps) {
            if (p.IsOptional) seenOptional = true;
            yield return BuildParameter(p, forceOptional: seenOptional && !p.IsOptional);
        }
    }

    static ParameterSyntax BuildParameter(ComQueryParam p, bool forceOptional = false)
    {
        // C# does not allow `ref` parameters to have default values, and does not allow
        // required parameters to follow optional ones.  In both cases we drop `ref` and
        // make the parameter optional.  See docs/com.md for the known caveats.
        bool makeOptional = p.IsOptional || forceOptional;
        bool useRef = p.IsOut && !makeOptional;
        var syntax = Parameter(Identifier(MakeSafeIdentifier(p.Name)))
        .WithType(ParseTypeName(useRef ? "ref " + p.Type : p.Type));
        if (makeOptional) {
            syntax = syntax.WithDefault(
                EqualsValueClause(
                    LiteralExpression(SyntaxKind.DefaultLiteralExpression,
                        Token(SyntaxKind.DefaultKeyword))));
        }

        return syntax;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Default-property chain resolution helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Follows the chain of no-parameter default properties (DISPID 0) starting from
    /// <paramref name="typeName"/> until no further no-param default exists, and returns
    /// the terminal return type.  This mirrors VB6's implicit default-member resolution:
    /// <c>rs!SomeColumn</c> → <c>rs.Fields["SomeColumn"].Value</c>, where <c>.Value</c>
    /// is the no-param default property of <c>Field</c>.
    /// <para>
    /// A visited-set guards against cycles in pathological type libraries.
    /// </para>
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
    /// A plain property setter (e.g. <c>Field.Value = x</c>) has exactly one parameter in the
    /// COM type library — the value to assign — which is never an index.  We therefore accept
    /// setters with zero or one parameters; setters with two or more parameters carry at least
    /// one index parameter and belong to parameterised indexers, not plain default properties.
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
