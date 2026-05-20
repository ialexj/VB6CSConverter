#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace ComStubGenerator;

/// <summary>
/// Identifies .NET runtime type-library GUIDs (mscorlib and System.*) so that
/// <see cref="MscorlibTypeNormalizingRewriter"/> is applied only to COM libraries
/// that reference them.
/// </summary>
internal static class DotnetLibraryGuids
{
    /// <summary>
    /// Returns <see langword="true"/> when any of the library's discovered dependencies
    /// is a .NET runtime type library (mscorlib or a System.* assembly).
    /// </summary>
    public static bool RequiresNormalization(LibraryModel library)
        => library.DiscoveredDependencies.Any(d => _guids.Contains(d.Guid));

    /// <summary>Returns <see langword="true"/> when <paramref name="guid"/> identifies a .NET runtime type library.</summary>
    public static bool Contains(Guid guid) => _guids.Contains(guid);

    // Well-known .NET Framework type-library GUIDs produced by tlbexp.exe.
    // These are fixed values — identical across all .NET Framework installations.
    static readonly HashSet<Guid> _guids =
    [
        new("BED7F4EA-1A96-11d2-8F08-00A0C9A6186D"),  // mscorlib
        new("BEE4BFEC-6683-3E67-9167-3C0CBC68F40A"),  // System (System.dll)
        new("4FB2D46F-EFC8-4643-BCD0-6E5BFA6A174C"),  // System.EnterpriseServices
        new("215D64D2-031C-33C7-96E3-61794CD1EE61"),  // System.Windows.Forms
        new("D37E2A3E-8545-3A39-9F4F-31827C9124AB"),  // System.Drawing
    ];
}

/// <summary>
/// Post-processes a generated C# stub <see cref="CompilationUnitSyntax"/> and rewrites
/// mscorlib / System.* type references to their canonical .NET equivalents.
/// Applied only to COM libraries whose <see cref="LibraryModel.DiscoveredDependencies"/>
/// include a .NET runtime type library (detected via <see cref="DotnetLibraryGuids"/>).
/// </summary>
internal sealed class MscorlibTypeNormalizingRewriter : CSharpSyntaxRewriter
{
    // ── mscorlib type lookup ─────────────────────────────────────────────

    static IEnumerable<string> GetDotnetTypeKeys(string ns, string name)
    {
        if (ns == "System")
            return [name, $"{ns}.{name}"];
        else
            return [name, ns.Replace(".", "") + "." + name, $"{ns}.{name}"];
    }

    static IEnumerable<(string Key, string FullName)> ReadTypesFromAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) yield break;
        var metadata = peReader.GetMetadataReader();

        // Type definitions — actual types defined in this assembly
        foreach (var handle in metadata.TypeDefinitions)
        {
            var td = metadata.GetTypeDefinition(handle);
            // Only top-level public types; nested types have VisibilityMask > Public (0x1)
            if ((td.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
            string ns = metadata.GetString(td.Namespace);
            string name = metadata.GetString(td.Name);
            if (string.IsNullOrEmpty(ns) || name.Length == 0 || name[0] == '<' || name.Contains('`')) continue;
            string fullName = $"{ns}.{name}";
            foreach (string key in GetDotnetTypeKeys(ns, name))
                yield return (key, fullName);
        }

        // Exported types — type forwarders in facade assemblies (e.g. System.Drawing.dll)
        // that contain no TypeDefinitions but forward everything to implementation assemblies
        foreach (var handle in metadata.ExportedTypes)
        {
            var et = metadata.GetExportedType(handle);
            string ns = metadata.GetString(et.Namespace);
            string name = metadata.GetString(et.Name);
            if (string.IsNullOrEmpty(ns) || name.Length == 0 || name[0] == '<' || name.Contains('`')) continue;
            string fullName = $"{ns}.{name}";
            foreach (string key in GetDotnetTypeKeys(ns, name))
                yield return (key, fullName);
        }
    }

    static IEnumerable<string> GetDotnetAssemblyPaths()
    {
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib.dll",
            "System.dll",
            "System.Windows.Forms.dll",
            "System.Drawing.dll",
        };

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // TRUSTED_PLATFORM_ASSEMBLIES lists all runtime assembly paths available to
        // this process, without requiring any of them to be loaded into the AppDomain.
        // This covers the active runtime band (Microsoft.NETCore.App).
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string path in tpa.Split(Path.PathSeparator))
            {
                string fileName = Path.GetFileName(path);
                if (targetNames.Contains(fileName) && found.Add(fileName))
                    yield return path;
            }
        }

        if (found.Count == targetNames.Count) yield break;

        // TPA only includes the active runtime band. When targeting net10.0 (not
        // net10.0-windows), WinForms/Drawing live in Microsoft.WindowsDesktop.App,
        // which is a sibling of Microsoft.NETCore.App under the dotnet shared dir.
        // Navigate: …/dotnet/shared/Microsoft.NETCore.App/<ver>/ → …/dotnet/shared/
        string coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string? sharedDir = Path.GetDirectoryName(Path.GetDirectoryName(coreDir));
        if (sharedDir == null) yield break;

        string desktopDir = Path.Combine(sharedDir, "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(desktopDir)) yield break;

        // Pick the highest installed version
        string? versionDir = Directory.EnumerateDirectories(desktopDir)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (versionDir == null) yield break;

        foreach (string target in targetNames)
        {
            if (found.Contains(target)) continue;
            string candidate = Path.Combine(versionDir, target);
            if (File.Exists(candidate))
                yield return candidate;
        }
    }

    static readonly ILookup<string, string> _mscorlibTypes = GetDotnetAssemblyPaths()
        .SelectMany(ReadTypesFromAssembly)
        .ToLookup(t => t.Key, t => t.FullName, StringComparer.Ordinal);

    // ── normalization logic ──────────────────────────────────────────────

    static string NormalizeDotnetTypeName(string name)
    {
        string ns = string.Empty;
        string typeName = name;

        if (name.Contains('.')) {
            var parts = name.Split('.', 2);
            (ns, typeName) = (parts[0], parts[1]);
        }

        typeName = typeName.TrimStart('_');

        if (ns.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
            || ns.Equals("System", StringComparison.OrdinalIgnoreCase)) {
            ns = string.Empty;
        }
        else if (!string.IsNullOrEmpty(ns) && !ns.StartsWith("System")) {
            return name; // Not a System.* namespace, so we won't find it in mscorlib. Leave as-is.
        }

        if (!string.IsNullOrEmpty(ns) && _mscorlibTypes[$"{ns}.{typeName}"].FirstOrDefault() is string fullName1)
            return fullName1;
        if (_mscorlibTypes[typeName].FirstOrDefault() is string fullName2)
            return fullName2;

        return name;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    static TypeSyntax NormalizeType(TypeSyntax type)
    {
        string text = type.ToString();
        string normalized = NormalizeDotnetTypeName(text);
        if (normalized == text) return type;
        return ParseTypeName(normalized).WithTriviaFrom(type);
    }

    // ── visitor overrides (type positions only) ──────────────────────────

    /// <summary>Normalizes base class / interface names in inheritance lists.</summary>
    public override SyntaxNode? VisitSimpleBaseType(SimpleBaseTypeSyntax node)
        => node.WithType(NormalizeType(node.Type));

    /// <summary>
    /// After normalizing each base type name, moves <c>Exception</c> (or <c>System.Exception</c>)
    /// to the first position so it satisfies the C# rule that a base class must precede interfaces.
    /// </summary>
    public override SyntaxNode? VisitBaseList(BaseListSyntax node)
    {
        var result = (BaseListSyntax)base.VisitBaseList(node)!;
        var types = result.Types;

        int exceptionIdx = -1;
        for (int i = 0; i < types.Count; i++) {
            if (types[i] is SimpleBaseTypeSyntax s && IsExceptionType(s.Type.ToString())) {
                exceptionIdx = i;
                break;
            }
        }

        if (exceptionIdx <= 0) return result; // already first, or not present

        var exceptionBase = types[exceptionIdx];
        var reordered = types.RemoveAt(exceptionIdx).Insert(0, exceptionBase);
        return result.WithTypes(reordered);
    }

    static bool IsExceptionType(string name) => name is "Exception" or "System.Exception";

    /// <summary>Normalizes parameter types, unwrapping <c>ref</c> when present.</summary>
    public override SyntaxNode? VisitParameter(ParameterSyntax node)
    {
        if (node.Type == null) return node;
        // Unwrap ref so we normalize only the inner type, not "ref mscorlib.X"
        if (node.Type is RefTypeSyntax refType)
            return node.WithType(refType.WithType(NormalizeType(refType.Type)));
        return node.WithType(NormalizeType(node.Type));
    }

    /// <summary>Normalizes method return types (parameters are handled via <see cref="VisitParameter"/>).</summary>
    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        return result.WithReturnType(NormalizeType(result.ReturnType));
    }

    /// <summary>Normalizes property types.</summary>
    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var result = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;
        return result.WithType(NormalizeType(result.Type));
    }

    /// <summary>Normalizes field types (covers struct fields generated from COM records).</summary>
    public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        var result = (VariableDeclarationSyntax)base.VisitVariableDeclaration(node)!;
        return result.WithType(NormalizeType(result.Type));
    }
}
