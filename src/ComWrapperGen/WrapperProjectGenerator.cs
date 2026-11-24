using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices.ComTypes;

namespace ComWrapperGen;

public static class WrapperProjectGenerator
{
    const int DispIdQueryInterface = 0x60000000;
    const int DispIdAddRef = 0x60000001;
    const int DispIdRelease = 0x60000002;
    const int DispIdGetTypeInfoCount = 0x60010000;
    const int DispIdGetTypeInfo = 0x60010001;
    const int DispIdGetIDsOfNames = 0x60010002;
    const int DispIdInvoke = 0x60010003;

    static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal) {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
        "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while"
    };

    static readonly HashSet<string> PrimitiveManagedTypes = new(StringComparer.Ordinal) {
        "void", "bool", "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "string", "DateTime", "object"
    };

    public static void Generate(ComQueryLibrary library, string outputDir, bool force)
    {
        if (library.Types is null || library.Types.Count == 0) {
            throw new InvalidOperationException($"Library '{library.Name}' did not contain any types.");
        }

        if (Directory.Exists(outputDir) && !force) {
            bool hasFiles = Directory.EnumerateFileSystemEntries(outputDir).Any();
            if (hasFiles) {
                throw new InvalidOperationException(
                    $"Output directory '{outputDir}' is not empty. Use --force to overwrite generated files.");
            }
        }

        Directory.CreateDirectory(outputDir);
        string internalDir = Path.Combine(outputDir, "internal");
        Directory.CreateDirectory(internalDir);

        string rootNamespace = ReferenceNaming.MakeSafeName(library.Name);
        string projectName = rootNamespace;
        var publicTypeNameMap = BuildPublicTypeNameMap(library, rootNamespace);

        WriteProjectFile(outputDir, projectName, rootNamespace);
        WriteRuntimeSupport(outputDir, rootNamespace);

        var coclasses = library.Types
            .Where(t => t.Kind == LibraryTypeKind.Class)
            .ToList();

        var publicInterfaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var internalInterfacesByPublic = new Dictionary<string, string>(StringComparer.Ordinal);
        var interfaceTypesByPublic = new Dictionary<string, ComQueryType>(StringComparer.Ordinal);

        foreach (var type in library.Types.Where(IsInterfaceType)) {
            string sanitized = SanitizeIdentifier(type.Name);
            string publicName = sanitized;
            string internalName = sanitized + "Internal";

            WriteInternalInterface(internalDir, rootNamespace, internalName, type);
            WritePublicInterface(outputDir, rootNamespace, publicName, type, publicTypeNameMap);

            publicInterfaces[type.Name] = publicName;
            internalInterfacesByPublic[publicName] = internalName;
            interfaceTypesByPublic[publicName] = type;
        }

        foreach (var pair in interfaceTypesByPublic) {
            WriteInterfaceProxy(
                outputDir,
                rootNamespace,
                pair.Key,
                internalInterfacesByPublic[pair.Key],
                pair.Value,
                publicTypeNameMap,
                internalInterfacesByPublic);
        }

        foreach (var coclass in coclasses) {
            WriteCoclassWrapper(outputDir, rootNamespace, coclass, publicInterfaces, publicTypeNameMap, internalInterfacesByPublic);
        }

        WriteGenerationReport(outputDir, library, coclasses.Count);
    }

    static bool IsInterfaceType(ComQueryType type)
        => type.Kind == LibraryTypeKind.Interface || type.Kind == LibraryTypeKind.DispatchInterface;

    static Dictionary<string, string> BuildPublicTypeNameMap(ComQueryLibrary library, string rootNamespace)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in library.Types ?? []) {
            if (type.Kind == LibraryTypeKind.Class) {
                continue;
            }

            string publicName = SanitizeIdentifier(type.Name);
            map[type.Name] = publicName;
            map[$"{rootNamespace}.{type.Name}"] = publicName;
        }

        return map;
    }

    static void WriteProjectFile(string outputDir, string projectName, string rootNamespace)
    {
        string csprojPath = Path.Combine(outputDir, projectName + ".csproj");
                string content = $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AssemblyName>{projectName}</AssemblyName>
    <RootNamespace>{rootNamespace}</RootNamespace>
    <Platforms>x86;x64</Platforms>
  </PropertyGroup>
</Project>
""";
        File.WriteAllText(csprojPath, content, Encoding.UTF8);
    }

    static void WriteRuntimeSupport(string outputDir, string rootNamespace)
    {
        string path = Path.Combine(outputDir, "internal", "RuntimeSupport.cs");
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using System.Runtime.InteropServices.Marshalling;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Internal;");
        sb.AppendLine();
        sb.AppendLine("internal static partial class NativeMethods");
        sb.AppendLine("{");
        sb.AppendLine("    internal const uint CLSCTX_INPROC_SERVER = 0x1;");
        sb.AppendLine("    internal const uint CLSCTX_INPROC_HANDLER = 0x2;");
        sb.AppendLine("    internal const uint CLSCTX_LOCAL_SERVER = 0x4;");
        sb.AppendLine("    internal const uint CLSCTX_REMOTE_SERVER = 0x10;");
        sb.AppendLine("    internal const uint CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER;");
        sb.AppendLine();
        sb.AppendLine("    [LibraryImport(\"ole32.dll\")]");
        sb.AppendLine("    internal static partial int CoCreateInstance(");
        sb.AppendLine("        in Guid rclsid,");
        sb.AppendLine("        IntPtr pUnkOuter,");
        sb.AppendLine("        uint dwClsContext,");
        sb.AppendLine("        in Guid riid,");
        sb.AppendLine("        out IntPtr ppv);");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("internal static class ComRuntime");
        sb.AppendLine("{");
        sb.AppendLine("    private static readonly StrategyBasedComWrappers Wrappers = new();");
        sb.AppendLine();
        sb.AppendLine("    internal static TInterface CreateInstance<TInterface>(Guid clsid)");
        sb.AppendLine("        where TInterface : class");
        sb.AppendLine("    {");
        sb.AppendLine("        Guid iid = typeof(TInterface).GUID;");
        sb.AppendLine("        int hr = NativeMethods.CoCreateInstance(in clsid, IntPtr.Zero, NativeMethods.CLSCTX_ALL, in iid, out IntPtr ptr);");
        sb.AppendLine("        if (hr < 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            Marshal.ThrowExceptionForHR(hr);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            object rcw = Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);");
        sb.AppendLine("            return (TInterface)rcw;");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (ptr != IntPtr.Zero)");
        sb.AppendLine("            {");
        sb.AppendLine("                Marshal.Release(ptr);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    internal static TInterface FromAbi<TInterface>(IntPtr ptr)");
        sb.AppendLine("        where TInterface : class");
        sb.AppendLine("    {");
        sb.AppendLine("        if (ptr == IntPtr.Zero)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"COM method returned a null interface pointer.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            object rcw = Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);");
        sb.AppendLine("            return (TInterface)rcw;");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            Marshal.Release(ptr);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static void WriteInternalInterface(string internalDir, string rootNamespace, string internalName, ComQueryType type)
    {
        string path = Path.Combine(internalDir, internalName + ".cs");
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using System.Runtime.InteropServices.Marshalling;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Internal;");
        sb.AppendLine();
        sb.AppendLine("[GeneratedComInterface]");
        if (type.Guid != Guid.Empty) {
            sb.AppendLine($"[Guid(\"{type.Guid:D}\")]");
        }
        sb.AppendLine("[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]");
        sb.AppendLine($"internal partial interface {internalName}");
        sb.AppendLine("{");

        foreach (var member in type.Members ?? []) {
            if (member.Kind == LibraryMemberKind.Field) {
                continue;
            }

            if (IsUnsupportedInternalMember(member)) {
                continue;
            }

            string methodName = GetInternalMethodName(member);
            string returnType = MapInternalType(member.ReturnType);
            string parameters = string.Join(", ", member.Parameters.Select(FormatInternalParameter));
            sb.Append("    ");
            if (member.DispId is int dispid) {
                sb.Append($"[DispId({dispid})] ");
            }

            sb.Append("[PreserveSig] ");
            if (returnType == "string") {
                sb.Append("[return: MarshalAs(UnmanagedType.BStr)] ");
            }
            else if (returnType == "bool") {
                sb.Append("[return: MarshalAs(UnmanagedType.VariantBool)] ");
            }
            sb.AppendLine($"{returnType} {methodName}({parameters});");
        }

        sb.AppendLine("}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static void WritePublicInterface(
        string outputDir,
        string rootNamespace,
        string publicName,
        ComQueryType type,
        IReadOnlyDictionary<string, string> publicTypeNameMap)
    {
        string path = Path.Combine(outputDir, publicName + ".cs");
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public interface {publicName}");
        sb.AppendLine("{");

        var groups = (type.Members ?? [])
            .Where(m => m.Kind is LibraryMemberKind.Method or LibraryMemberKind.PropertyGet or LibraryMemberKind.PropertySet)
            .Where(m => !IsComPlumbingMember(m))
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups) {
            var get = group.FirstOrDefault(x => x.Kind == LibraryMemberKind.PropertyGet && x.Parameters.Count == 0);
            var set = group.FirstOrDefault(x => x.Kind == LibraryMemberKind.PropertySet && x.Parameters.Count == 1);
            if (get is not null || set is not null) {
                string propertyName = SanitizeIdentifier(group.Key);
                string propertyType = get is not null
                    ? MapManagedType(get.ReturnType, publicTypeNameMap)
                    : MapManagedParameterType(set!.Parameters[0], publicTypeNameMap);

                if (set is not null && IsReadOnlySet(set)) {
                    set = null;
                }

                sb.AppendLine($"    {propertyType} {propertyName} {{ {(get is not null ? "get;" : string.Empty)} {(set is not null ? "set;" : string.Empty)} }}");
                continue;
            }

            foreach (var member in group) {
                if (member.Kind != LibraryMemberKind.Method) {
                    continue;
                }

                string methodName = SanitizeIdentifier(member.Name);
                string returnType = MapManagedType(member.ReturnType, publicTypeNameMap);
                string parameters = string.Join(", ", member.Parameters.Select(p => FormatParameter(p, publicTypeNameMap)));
                sb.AppendLine($"    {returnType} {methodName}({parameters});");
            }
        }

        bool hasNewEnum = (type.Members ?? []).Any(m => m.DispId == -4);
        if (hasNewEnum) {
            sb.AppendLine("    IEnumerable<object> AsEnumerable();");
        }

        sb.AppendLine("}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static void WriteCoclassWrapper(
        string outputDir,
        string rootNamespace,
        ComQueryType coclass,
        IReadOnlyDictionary<string, string> publicInterfaces,
        IReadOnlyDictionary<string, string> publicTypeNameMap,
        IReadOnlyDictionary<string, string> internalInterfacesByPublic)
    {
        string className = SanitizeIdentifier(coclass.Name);
        string path = Path.Combine(outputDir, className + ".cs");

        string? defaultInterfaceName = coclass.ImplementedInterfaces?
            .Select(x => publicInterfaces.TryGetValue(x, out string? publicName) ? publicName : null)
            .FirstOrDefault(x => x is not null);

        if (defaultInterfaceName is null) {
            return;
        }

        string interfaceName = defaultInterfaceName;
        string internalName = interfaceName + "Internal";

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine($"using {rootNamespace}.Internal;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {className} : {interfaceName}");
        sb.AppendLine("{");
        if (coclass.Guid != Guid.Empty) {
            sb.AppendLine($"    private static readonly Guid CLSID = new(\"{coclass.Guid:D}\");");
        }
        else {
            sb.AppendLine("    private static readonly Guid CLSID = Guid.Empty;");
        }
        sb.AppendLine($"    private readonly {internalName} _inner;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}()");
        sb.AppendLine("    {");
        sb.AppendLine("        _inner = ComRuntime.CreateInstance<" + internalName + ">(CLSID);");
        sb.AppendLine("    }");

        var members = coclass.Members ?? [];

        var defaultMember = members
            .FirstOrDefault(m => m.IsDefault && m.Kind == LibraryMemberKind.PropertyGet && m.Parameters.Count == 0);
        if (defaultMember is not null) {
            string conversionType = MapManagedType(defaultMember.ReturnType, publicTypeNameMap);
            string internalReturnType = MapInternalType(defaultMember.ReturnType);
            bool isInterfaceConversion = publicInterfaces.Values.Any(v => string.Equals(v, conversionType, StringComparison.Ordinal));
            if (conversionType is not "void" and not "object" && !isInterfaceConversion) {
                sb.AppendLine();
                sb.AppendLine($"    public static implicit operator {conversionType}({className} value)");
                sb.AppendLine("    {");
                if (NeedsAbiConversion(conversionType, internalReturnType)) {
                    string expr = BuildAbiReturnConversionExpression(
                        conversionType,
                        $"value._inner.{GetInternalMethodName(defaultMember)}()",
                        internalInterfacesByPublic);
                    sb.AppendLine($"        return {expr};");
                }
                else {
                    sb.AppendLine($"        return value._inner.{GetInternalMethodName(defaultMember)}();");
                }
                sb.AppendLine("    }");
            }
        }

        var emittedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members.Where(m => m.Kind != LibraryMemberKind.Field && !IsComPlumbingMember(m))) {
            string internalMethod = GetInternalMethodName(member);
            if (member.Kind == LibraryMemberKind.PropertyGet && member.Parameters.Count == 0) {
                string name = SanitizeIdentifier(member.Name);
                if (!emittedProperties.Add(name)) {
                    continue;
                }

                string type = MapManagedType(member.ReturnType, publicTypeNameMap);
                string internalReturnType = MapInternalType(member.ReturnType);
                var setter = members.FirstOrDefault(m =>
                    m.Kind == LibraryMemberKind.PropertySet
                    && string.Equals(m.Name, member.Name, StringComparison.OrdinalIgnoreCase)
                    && m.Parameters.Count == 1);
                sb.AppendLine();
                sb.AppendLine($"    public {type} {name}");
                sb.AppendLine("    {");
                if (NeedsAbiConversion(type, internalReturnType)) {
                    string expr = BuildAbiReturnConversionExpression(type, $"_inner.{internalMethod}()", internalInterfacesByPublic);
                    sb.AppendLine($"        get => {expr};");
                }
                else {
                    sb.AppendLine($"        get => _inner.{internalMethod}();");
                }
                if (setter is not null && !IsReadOnlySet(setter)) {
                    string setMethod = GetInternalMethodName(setter);
                    if (MapInternalType(setter.Parameters[0].Type) == "nint") {
                        sb.AppendLine($"        set => _inner.{setMethod}((dynamic)value);");
                    }
                    else {
                        sb.AppendLine($"        set => _inner.{setMethod}(value);");
                    }
                }
                sb.AppendLine("    }");
                continue;
            }

            if (member.Kind == LibraryMemberKind.PropertySet && member.Parameters.Count == 1) {
                string name = SanitizeIdentifier(member.Name);
                if (!emittedProperties.Add(name)) {
                    continue;
                }

                if (IsReadOnlySet(member)) {
                    continue;
                }

                string type = MapManagedParameterType(member.Parameters[0], publicTypeNameMap);
                sb.AppendLine();
                sb.AppendLine($"    public {type} {name}");
                sb.AppendLine("    {");
                if (MapInternalType(member.Parameters[0].Type) == "nint") {
                    sb.AppendLine($"        set => _inner.{internalMethod}((dynamic)value);");
                }
                else {
                    sb.AppendLine($"        set => _inner.{internalMethod}(value);");
                }
                sb.AppendLine("    }");
                continue;
            }

            if (member.Kind != LibraryMemberKind.Method) {
                continue;
            }

            string methodName = SanitizeIdentifier(member.Name);
            string returnType = MapManagedType(member.ReturnType, publicTypeNameMap);
            string methodInternalReturnType = MapInternalType(member.ReturnType);
            string paramDecl = string.Join(", ", member.Parameters.Select(p => FormatParameter(p, publicTypeNameMap)));
            string invocationArgs = string.Join(", ", member.Parameters.Select(FormatInvocationArgument));
            sb.AppendLine();
            sb.AppendLine($"    public {returnType} {methodName}({paramDecl})");
            sb.AppendLine("    {");
            if (returnType == "void") {
                sb.AppendLine($"        _inner.{internalMethod}({invocationArgs});");
            }
            else if (NeedsAbiConversion(returnType, methodInternalReturnType)) {
                string expr = BuildAbiReturnConversionExpression(returnType, $"_inner.{internalMethod}({invocationArgs})", internalInterfacesByPublic);
                sb.AppendLine($"        return {expr};");
            }
            else {
                sb.AppendLine($"        return _inner.{internalMethod}({invocationArgs});");
            }
            sb.AppendLine("    }");
        }

        bool hasNewEnum = members.Any(m => m.DispId == -4)
            || members.Any(m => string.Equals(m.Name, "GetEnumerator", StringComparison.OrdinalIgnoreCase));
        if (hasNewEnum) {
            sb.AppendLine();
            sb.AppendLine("    public IEnumerable<object> AsEnumerable()");
            sb.AppendLine("    {");
            sb.AppendLine("        throw new NotSupportedException(\"AsEnumerable adapter is not implemented in this generation slice yet.\");");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static void WriteInterfaceProxy(
        string outputDir,
        string rootNamespace,
        string interfaceName,
        string internalName,
        ComQueryType type,
        IReadOnlyDictionary<string, string> publicTypeNameMap,
        IReadOnlyDictionary<string, string> internalInterfacesByPublic)
    {
        string path = Path.Combine(outputDir, interfaceName + "Proxy.cs");
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine($"using {rootNamespace}.Internal;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();
        sb.AppendLine($"internal sealed class {interfaceName}Proxy : {interfaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {internalName} _inner;");
        sb.AppendLine();
        sb.AppendLine($"    internal {interfaceName}Proxy({internalName} inner)");
        sb.AppendLine("    {");
        sb.AppendLine("        _inner = inner;");
        sb.AppendLine("    }");

        var members = type.Members ?? [];
        var emittedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members.Where(m => m.Kind != LibraryMemberKind.Field && !IsComPlumbingMember(m))) {
            string internalMethod = GetInternalMethodName(member);
            if (member.Kind == LibraryMemberKind.PropertyGet && member.Parameters.Count == 0) {
                string name = SanitizeIdentifier(member.Name);
                if (!emittedProperties.Add(name)) {
                    continue;
                }

                string propertyType = MapManagedType(member.ReturnType, publicTypeNameMap);
                string internalReturnType = MapInternalType(member.ReturnType);
                var setter = members.FirstOrDefault(m =>
                    m.Kind == LibraryMemberKind.PropertySet
                    && string.Equals(m.Name, member.Name, StringComparison.OrdinalIgnoreCase)
                    && m.Parameters.Count == 1);

                sb.AppendLine();
                sb.AppendLine($"    public {propertyType} {name}");
                sb.AppendLine("    {");
                if (NeedsAbiConversion(propertyType, internalReturnType)) {
                    string expr = BuildAbiReturnConversionExpression(propertyType, $"_inner.{internalMethod}()", internalInterfacesByPublic);
                    sb.AppendLine($"        get => {expr};");
                }
                else {
                    sb.AppendLine($"        get => _inner.{internalMethod}();");
                }

                if (setter is not null && !IsReadOnlySet(setter)) {
                    string setMethod = GetInternalMethodName(setter);
                    if (MapInternalType(setter.Parameters[0].Type) == "nint") {
                        sb.AppendLine($"        set => _inner.{setMethod}((dynamic)value);");
                    }
                    else {
                        sb.AppendLine($"        set => _inner.{setMethod}(value);");
                    }
                }

                sb.AppendLine("    }");
                continue;
            }

            if (member.Kind == LibraryMemberKind.PropertySet && member.Parameters.Count == 1) {
                string name = SanitizeIdentifier(member.Name);
                if (!emittedProperties.Add(name)) {
                    continue;
                }

                if (IsReadOnlySet(member)) {
                    continue;
                }

                string propertyType = MapManagedParameterType(member.Parameters[0], publicTypeNameMap);
                sb.AppendLine();
                sb.AppendLine($"    public {propertyType} {name}");
                sb.AppendLine("    {");
                if (MapInternalType(member.Parameters[0].Type) == "nint") {
                    sb.AppendLine($"        set => _inner.{internalMethod}((dynamic)value);");
                }
                else {
                    sb.AppendLine($"        set => _inner.{internalMethod}(value);");
                }

                sb.AppendLine("    }");
                continue;
            }

            if (member.Kind != LibraryMemberKind.Method) {
                continue;
            }

            string methodName = SanitizeIdentifier(member.Name);
            string returnType = MapManagedType(member.ReturnType, publicTypeNameMap);
            string methodInternalReturnType = MapInternalType(member.ReturnType);
            string paramDecl = string.Join(", ", member.Parameters.Select(p => FormatParameter(p, publicTypeNameMap)));
            string invocationArgs = string.Join(", ", member.Parameters.Select(FormatInvocationArgument));

            sb.AppendLine();
            sb.AppendLine($"    public {returnType} {methodName}({paramDecl})");
            sb.AppendLine("    {");
            if (returnType == "void") {
                sb.AppendLine($"        _inner.{internalMethod}({invocationArgs});");
            }
            else if (NeedsAbiConversion(returnType, methodInternalReturnType)) {
                string expr = BuildAbiReturnConversionExpression(returnType, $"_inner.{internalMethod}({invocationArgs})", internalInterfacesByPublic);
                sb.AppendLine($"        return {expr};");
            }
            else {
                sb.AppendLine($"        return _inner.{internalMethod}({invocationArgs});");
            }
            sb.AppendLine("    }");
        }

        bool hasNewEnum = members.Any(m => m.DispId == -4)
            || members.Any(m => string.Equals(m.Name, "GetEnumerator", StringComparison.OrdinalIgnoreCase));
        if (hasNewEnum) {
            sb.AppendLine();
            sb.AppendLine("    public IEnumerable<object> AsEnumerable()");
            sb.AppendLine("    {");
            sb.AppendLine("        throw new NotSupportedException(\"AsEnumerable adapter is not implemented in this generation slice yet.\");");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static string BuildAbiReturnConversionExpression(
        string managedType,
        string abiExpression,
        IReadOnlyDictionary<string, string> internalInterfacesByPublic)
    {
        if (managedType.EndsWith("[]", StringComparison.Ordinal)) {
            return "(dynamic)" + abiExpression;
        }

        if (internalInterfacesByPublic.TryGetValue(managedType, out string? internalName)) {
            return $"new {managedType}Proxy(ComRuntime.FromAbi<{internalName}>({abiExpression}))";
        }

        return "(dynamic)" + abiExpression;
    }

    static void WriteGenerationReport(string outputDir, ComQueryLibrary library, int coclassCount)
    {
        string path = Path.Combine(outputDir, "_GenerationReport.txt");
        var sb = new StringBuilder();
        sb.AppendLine("ComWrapperGen generation report");
        sb.AppendLine($"Library: {library.Name}");
        sb.AppendLine($"GUID: {library.Guid:B}");
        sb.AppendLine($"Version: {library.Major}.{library.Minor}");
        sb.AppendLine($"Types: {library.Types?.Count ?? 0}");
        sb.AppendLine($"Coclasses: {coclassCount}");
        sb.AppendLine();
        sb.AppendLine("Notes:");
        sb.AppendLine("- This is the first runtime wrapper generation slice.");
        sb.AppendLine("- Vtable/ABI validation and dispatch/event adapters are not complete yet.");
        sb.AppendLine("- No x86/x64 metadata merge is performed.");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static bool IsReadOnlySet(ComQueryMember setMember)
    {
        string? desc = setMember.Description;
        if (string.IsNullOrWhiteSpace(desc)) {
            return false;
        }

        return desc.Contains("readonly", StringComparison.OrdinalIgnoreCase);
    }

    static string GetInternalMethodName(ComQueryMember member)
    {
        string baseName = SanitizeIdentifier(member.Name);
        return member.Kind switch {
            LibraryMemberKind.PropertyGet => "get_" + baseName,
            LibraryMemberKind.PropertySet => "set_" + baseName,
            _ => baseName,
        };
    }

    static string FormatParameter(ComQueryParam param, IReadOnlyDictionary<string, string> publicTypeNameMap)
    {
        string modifier = param.IsOut ? "out " : string.Empty;
        string type = MapManagedParameterType(param, publicTypeNameMap);
        string name = SanitizeIdentifier(param.Name);
        return modifier + type + " " + name;
    }

    static string MapManagedParameterType(ComQueryParam param, IReadOnlyDictionary<string, string> publicTypeNameMap)
    {
        return MapInternalType(param.Type) == "nint"
            ? "dynamic"
            : MapManagedType(param.Type, publicTypeNameMap);
    }

    static string FormatInvocationArgument(ComQueryParam param)
    {
        string name = SanitizeIdentifier(param.Name);
        if (param.IsOut) {
            return "out " + name;
        }

        return MapInternalType(param.Type) == "nint"
            ? "(dynamic)" + name
            : name;
    }

    static string FormatInternalParameter(ComQueryParam param)
    {
        string name = SanitizeIdentifier(param.Name);
        string type = MapInternalType(param.Type);
        string modifier = param.IsOut ? "out " : string.Empty;

        if (type == "bool") {
            type = "[MarshalAs(UnmanagedType.VariantBool)] bool";
        }
        else if (type == "string") {
            type = "[MarshalAs(UnmanagedType.BStr)] string";
        }

        return modifier + type + " " + name;
    }

    static string MapManagedType(string comType, IReadOnlyDictionary<string, string> publicTypeNameMap)
    {
        return comType switch {
            "void" => "void",
            "bool" => "bool",
            "sbyte" => "sbyte",
            "byte" => "byte",
            "short" => "short",
            "ushort" => "ushort",
            "int" => "int",
            "uint" => "uint",
            "long" => "long",
            "ulong" => "ulong",
            "float" => "float",
            "double" => "double",
            "decimal" => "decimal",
            "string" => "string",
            "System.DateTime" => "DateTime",
            "object" => "object",
            _ when comType.EndsWith("[]", StringComparison.Ordinal) => MapManagedType(comType[..^2], publicTypeNameMap) + "[]",
            _ => ResolvePublicNamedType(comType, publicTypeNameMap),
        };
    }

    static string ResolvePublicNamedType(string comType, IReadOnlyDictionary<string, string> publicTypeNameMap)
    {
        if (publicTypeNameMap.TryGetValue(comType, out string? mapped)) {
            return mapped;
        }

        int lastDot = comType.LastIndexOf('.');
        if (lastDot >= 0) {
            string shortName = comType[(lastDot + 1)..];
            if (publicTypeNameMap.TryGetValue(shortName, out string? mappedShort)) {
                return mappedShort;
            }

            // Unknown foreign type in first slice: keep compileability without dynamic callsites.
            return "object";
        }

        return "object";
    }

    static bool NeedsAbiConversion(string managedType, string internalType)
    {
        if (internalType != "nint") {
            return false;
        }

        return managedType.EndsWith("[]", StringComparison.Ordinal)
            || !PrimitiveManagedTypes.Contains(managedType);
    }

    static string MapInternalType(string comType)
    {
        return comType switch {
            "void" => "void",
            "bool" => "bool",
            "sbyte" => "sbyte",
            "byte" => "byte",
            "short" => "short",
            "ushort" => "ushort",
            "int" => "int",
            "uint" => "uint",
            "long" => "long",
            "ulong" => "ulong",
            "float" => "float",
            "double" => "double",
            "decimal" => "decimal",
            "string" => "string",
            "System.DateTime" => "double",
            "object" => "nint",
            _ when comType.EndsWith("[]", StringComparison.Ordinal) => "nint",
            _ => "nint",
        };
    }

    static bool IsComPlumbingMember(ComQueryMember member)
    {
        if (member.DispId is not int dispid) {
            return false;
        }

        return dispid is DispIdQueryInterface
            or DispIdAddRef
            or DispIdRelease
            or DispIdGetTypeInfoCount
            or DispIdGetTypeInfo
            or DispIdGetIDsOfNames
            or DispIdInvoke;
    }

    static bool IsUnsupportedInternalMember(ComQueryMember member)
    {
        if (IsComPlumbingMember(member)) {
            return true;
        }

        // First slice: avoid by-ref array/object/date signatures that need explicit custom marshalling.
        if (member.Parameters.Any(p => p.IsOut && p.Type.EndsWith("[]", StringComparison.Ordinal))) {
            return true;
        }

        return false;
    }

    static string SanitizeQualifiedTypeName(string fullName)
    {
        string[] parts = fullName.Split('.');
        for (int i = 0; i < parts.Length; i++) {
            parts[i] = SanitizeIdentifier(parts[i]);
        }

        return string.Join('.', parts);
    }

    static string SanitizeIdentifier(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            return "Unnamed";
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text) {
            if (char.IsLetterOrDigit(c) || c == '_') {
                sb.Append(c);
            }
        }

        if (sb.Length == 0) {
            sb.Append("Unnamed");
        }

        if (char.IsDigit(sb[0])) {
            sb.Insert(0, '_');
        }

        string result = sb.ToString();
        if (CSharpKeywords.Contains(result)) {
            return "@" + result;
        }

        return result;
    }
}
