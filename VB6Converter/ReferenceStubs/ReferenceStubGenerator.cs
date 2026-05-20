#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VB6Converter.RoslynHelpers;

namespace VB6Converter.ReferenceStubs;

/// <summary>
/// Generates C# stub source files from a <see cref="LibraryModel"/> and writes
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
    public static IReadOnlyList<string> Generate(LibraryModel library, string referenceRoot)
    {
        var written = new List<string>();
        var usedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string libDir = Path.Combine(referenceRoot, library.SafeName);
        Directory.CreateDirectory(libDir);

        // Pre-pass: compute emitted names and build the qualified-name → emitted-name map
        // for every struct in this library.  Used by cycle detection below.
        var structQualifiedToEmitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        {
            var prepassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in library.Types.OrderBy(t => t.Name)) {
                if (type.Kind == LibraryTypeKind.Alias) continue;
                string emitted = MakeUniqueName(MakeSafeIdentifier(type.Name), prepassNames);
                if (type.Kind == LibraryTypeKind.Struct)
                    structQualifiedToEmitted[$"{library.SafeName}.{type.Name}"] = emitted;
            }
        }
        var cyclicFields = DetectStructCycles(library, structQualifiedToEmitted);

        foreach (var type in library.Types.OrderBy(t => t.Name)) {
            if (type.Kind == LibraryTypeKind.Alias) continue;  // handled by CollectAliases / ReferenceUsingsGenerator

            string emittedTypeName = MakeUniqueName(MakeSafeIdentifier(type.Name), usedTypeNames);
            string? source = GenerateType(library, type, emittedTypeName, cyclicFields);
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
    public static IReadOnlyList<(string Name, string CSharpType)> CollectAliases(LibraryModel library)
    {
        var aliases = new List<(string Name, string CSharpType)>();
        foreach (var type in library.Types) {
            if (type.Kind == LibraryTypeKind.Alias && !string.IsNullOrWhiteSpace(type.AliasedCSharpType))
                aliases.Add((MakeSafeIdentifier(type.Name), type.AliasedCSharpType!));
        }
        return aliases;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Per-type dispatch
    // ──────────────────────────────────────────────────────────────────────

    static string? GenerateType(
        LibraryModel library,
        LibraryTypeModel type,
        string emittedTypeName,
        HashSet<(string TypeName, string FieldName)> cyclicFields)
    {
        MemberDeclarationSyntax? decl = type.Kind switch {
            LibraryTypeKind.Enum                                              => GenerateEnum(type, emittedTypeName),
            LibraryTypeKind.DispatchInterface or LibraryTypeKind.Interface     => GenerateInterface(type, emittedTypeName),
            LibraryTypeKind.Class or LibraryTypeKind.Module                    => GenerateClass(type, emittedTypeName),
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

        // Post-process: rewrite mscorlib/System.* type references to their canonical .NET
        // equivalents, but only for libraries that actually depend on those type libraries.
        if (DotnetLibraryGuids.RequiresNormalization(library)) {
            cu = (CompilationUnitSyntax)new MscorlibTypeNormalizingRewriter().Visit(cu)!;
            cu = cu.NormalizeWhitespace();
        }

        return cu.ToFullString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Enum
    // ──────────────────────────────────────────────────────────────────────

    static EnumDeclarationSyntax GenerateEnum(LibraryTypeModel type, string emittedTypeName)
    {
        bool needsLong = type.EnumValues.Any(v => v.Value < int.MinValue || v.Value > int.MaxValue);

        var members = type.EnumValues
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

    static InterfaceDeclarationSyntax GenerateInterface(LibraryTypeModel type, string emittedTypeName)
    {
        var memberDecls = new List<MemberDeclarationSyntax>();
        // Seed with the interface name to prevent CS0542 (member name same as enclosing type)
        var usedMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };

        var propertyGroups = type.Members
            .Where(m => m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet)
            .GroupBy(m => m.Name)
            .ToList();

        foreach (var group in propertyGroups.OrderBy(g => g.Key)) {
            var getter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
            var setter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertySet);
            string propertyName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);

            string propType = getter != null ? getter.ReturnCSharpType : "object";
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

            var prop = PropertyDeclaration(ParseTypeName(propType), Identifier(propertyName))
                .WithAccessorList(AccessorList(List(accessors)));

            memberDecls.Add(prop);
        }

        foreach (var method in type.Members
                     .Where(m => m.Kind == LibraryMemberKind.Method)
                     .OrderBy(m => m.Name)) {
            string methodName = MakeUniqueName(MakeSafeIdentifier(method.Name), usedMemberNames);
            var parameters = BuildParameters(method.Parameters).ToArray();

            var methodDecl = MethodDeclaration(
                    ParseTypeName(method.ReturnCSharpType),
                    Identifier(methodName))
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

            memberDecls.Add(methodDecl);
        }

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

    // ──────────────────────────────────────────────────────────────────────
    // Class / module
    // ──────────────────────────────────────────────────────────────────────

    static ClassDeclarationSyntax GenerateClass(LibraryTypeModel type, string emittedTypeName)
    {
        bool isStatic = type.Kind == LibraryTypeKind.Module;

        var memberDecls = new List<MemberDeclarationSyntax>();
        // Seed with the class name to prevent CS0542 (member name same as enclosing type)
        var usedMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };

        // Group by name + kind to collapse get/set pairs into one property
        var propertyGroups = type.Members
            .Where(m => m.Kind == LibraryMemberKind.PropertyGet || m.Kind == LibraryMemberKind.PropertySet)
            .GroupBy(m => m.Name)
            .ToList();

        var handledPropertyNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var group in propertyGroups.OrderBy(g => g.Key)) {
            var getter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertyGet);
            var setter = group.FirstOrDefault(m => m.Kind == LibraryMemberKind.PropertySet);
            string propertyName = MakeUniqueName(MakeSafeIdentifier(group.Key), usedMemberNames);

            string propType = getter != null ? getter.ReturnCSharpType : "object";
            if (propType == "void") propType = "object";

            var accessors = new List<AccessorDeclarationSyntax>();

            if (getter != null) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithBody(ThrowNotImplementedBody()));
            }

            if (setter != null) {
                accessors.Add(
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithBody(ThrowNotImplementedBody()));
            }

            var prop = PropertyDeclaration(ParseTypeName(propType), Identifier(propertyName))
                .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                .WithAccessorList(AccessorList(List(accessors)));

            memberDecls.Add(prop);
            handledPropertyNames.Add(group.Key);
        }

        // Methods (skip anything already emitted as property)
        foreach (var method in type.Members
                     .Where(m => m.Kind == LibraryMemberKind.Method)
                     .OrderBy(m => m.Name)) {
            string methodName = MakeUniqueName(MakeSafeIdentifier(method.Name), usedMemberNames);

            var parameters = BuildParameters(method.Parameters).ToArray();

            var methodDecl = MethodDeclaration(
                    ParseTypeName(method.ReturnCSharpType),
                    Identifier(methodName))
                .WithModifiers(Modifiers(isPublic: true, isStatic: isStatic))
                .WithParameterList(ParameterList(SeparatedList(parameters)))
                .WithBody(method.ReturnCSharpType == "void"
                    ? ThrowNotImplementedBody()
                    : ThrowNotImplementedReturnBody());

            memberDecls.Add(methodDecl);
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
    // Struct (TKIND_RECORD / TKIND_UNION)
    // ──────────────────────────────────────────────────────────────────────

    static StructDeclarationSyntax GenerateStruct(
        LibraryTypeModel type,
        string emittedTypeName,
        HashSet<(string TypeName, string FieldName)> cyclicFields)
    {
        // Seed with the struct name to prevent CS0542 (member name same as enclosing type)
        var usedFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emittedTypeName };
        var fieldDecls = new List<MemberDeclarationSyntax>();

        foreach (var field in type.Members.Where(m => m.Kind == LibraryMemberKind.Field)) {
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
                        Comment($"// was: {field.ReturnCSharpType} — COM pointer field; replaced with nint to avoid struct layout cycle")));
                continue;
            }

            fieldDecls.Add(
                FieldDeclaration(
                    VariableDeclaration(ParseTypeName(field.ReturnCSharpType))
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
        LibraryModel library,
        Dictionary<string, string> structQualifiedToEmitted)
    {
        // Build adjacency list: emittedTypeName → [(fieldName, emittedTargetTypeName)]
        // Only edges that point to other structs within the same library are included.
        var deps = new Dictionary<string, List<(string FieldName, string Target)>>(StringComparer.Ordinal);

        foreach (var type in library.Types.Where(t => t.Kind == LibraryTypeKind.Struct)) {
            string qualName = $"{library.SafeName}.{type.Name}";
            if (!structQualifiedToEmitted.TryGetValue(qualName, out string? emitted)) continue;

            var edges = new List<(string, string)>();
            foreach (var field in type.Members.Where(m => m.Kind == LibraryMemberKind.Field)) {
                if (structQualifiedToEmitted.TryGetValue(field.ReturnCSharpType, out string? targetEmitted))
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
    static IEnumerable<ParameterSyntax> BuildParameters(IReadOnlyList<LibraryParameterModel> ps)
    {
        bool seenOptional = false;
        foreach (var p in ps) {
            if (p.IsOptional) seenOptional = true;
            yield return BuildParameter(p, forceOptional: seenOptional && !p.IsOptional);
        }
    }

    static ParameterSyntax BuildParameter(LibraryParameterModel p, bool forceOptional = false)
    {
        // C# does not allow `ref` parameters to have default values, and does not allow
        // required parameters to follow optional ones.  In both cases we drop `ref` and
        // make the parameter optional.  See docs/com.md for the known caveats.
        bool makeOptional = p.IsOptional || forceOptional;
        bool useRef = p.IsOut && !makeOptional;
        var syntax = Parameter(Identifier(MakeSafeIdentifier(p.Name)))
        .WithType(ParseTypeName(useRef ? "ref " + p.CSharpType : p.CSharpType));
        if (makeOptional) {
            syntax = syntax.WithDefault(
                EqualsValueClause(
                    LiteralExpression(SyntaxKind.DefaultLiteralExpression,
                        Token(SyntaxKind.DefaultKeyword))));
        }

        return syntax;
    }

    static BlockSyntax ThrowNotImplementedBody() => Block(
        ThrowStatement(
            ObjectCreationExpression(
                QualifiedName(IdentifierName("System"), IdentifierName("NotImplementedException")))
                .WithArgumentList(ArgumentList())));

    static BlockSyntax ThrowNotImplementedReturnBody() => Block(
        ThrowStatement(
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

}
