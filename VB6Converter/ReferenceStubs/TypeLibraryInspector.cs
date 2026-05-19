#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using VB6Parser;

namespace VB6Converter.ReferenceStubs;

/// <summary>
/// Inspects COM type libraries using the Windows OLE Automation API and builds
/// <see cref="LibraryModel"/> instances that can be turned into C# stubs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TypeLibraryInspector
{
    // ──────────────────────────────────────────────────────────────────────
    // Native imports
    // ──────────────────────────────────────────────────────────────────────

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int LoadTypeLib([MarshalAs(UnmanagedType.LPWStr)] string szFile,
                                  [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.ITypeLib ppTLib);

    // Mirrors the native TLIBATTR; only the fields we need are declared.
    [StructLayout(LayoutKind.Sequential)]
    struct TLIBATTR
    {
        public Guid  guid;
        public int   lcid;
        public int   syskind;       // SYSKIND enum (int-sized)
        public short wMajorVerNum;
        public short wMinorVerNum;
        public short wLibFlags;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Public entry point
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the type library at <paramref name="path"/> and returns a
    /// <see cref="LibraryModel"/>, or <see langword="null"/> if the file
    /// cannot be loaded or parsed.
    /// </summary>
    public static LibraryModel? Inspect(VisualBasicProjectReference reference, string path)
    {
        if (!TryLoadTypeLibWithFallback(reference, path, out var typeLib, out string loadedFromPath, out int hr)) {
            Log.Default.Warning("TypeLibraryInspector: unable to load type library for {path}; last HRESULT 0x{hr:X8} ({reason})",
                path,
                hr,
                DescribeLoadTypeLibFailure(hr));
            return null;
        }

        if (!string.Equals(path, loadedFromPath, StringComparison.OrdinalIgnoreCase)) {
            Log.Default.Information("TypeLibraryInspector: using fallback type library path {loadedPath} for {path}", loadedFromPath, path);
        }

        try {
            return InspectTypeLib(typeLib, reference);
        }
        catch (Exception ex) {
            LogInspectionFailure(reference, loadedFromPath, "InspectTypeLib", ex);
            return null;
        }
        finally {
            Marshal.ReleaseComObject(typeLib);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type-library level
    // ──────────────────────────────────────────────────────────────────────

    static LibraryModel InspectTypeLib(System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
                                       VisualBasicProjectReference reference)
    {
        typeLib.GetDocumentation(-1, out string libName, out _, out _, out _);
        libName = string.IsNullOrWhiteSpace(libName) ? reference.Description : libName;

        string safeName = ReferenceNaming.MakeSafeName(libName);

        int typeCount = typeLib.GetTypeInfoCount();
        var types     = new List<LibraryTypeModel>(typeCount);
        var discoveredDeps = new HashSet<DiscoveredDependency>();

        for (int i = 0; i < typeCount; i++) {
            LibraryTypeModel? typeModel;
            try {
                typeModel = InspectTypeInfo(typeLib, i, discoveredDeps);
            }
            catch (Exception ex) {
                string typeName = TryGetTypeName(typeLib, i);
                Log.Default.Warning(ex,
                    "TypeLibraryInspector: skipping type index {index} ({typeName}) in library {libraryName} after inspection failure",
                    i,
                    typeName,
                    libName);
                continue;
            }

            if (typeModel != null) {
                types.Add(typeModel);
            }
        }

        return new LibraryModel(libName, safeName, reference.Guid, reference.MajorVersion, reference.MinorVersion, types, [.. discoveredDeps]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type-info level
    // ──────────────────────────────────────────────────────────────────────

    static LibraryTypeModel? InspectTypeInfo(System.Runtime.InteropServices.ComTypes.ITypeLib typeLib, int index, HashSet<DiscoveredDependency> discoveredDeps)
    {
        typeLib.GetTypeInfo(index, out var typeInfo);
        if (typeInfo == null) return null;

        string typeName = "<unknown>";
        IntPtr pTypeAttr = IntPtr.Zero;

        try {
            typeInfo.GetDocumentation(-1, out typeName, out _, out _, out _);
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            typeInfo.GetTypeAttr(out pTypeAttr);
            if (pTypeAttr == IntPtr.Zero) return null;

            var typeAttr = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pTypeAttr);

            LibraryTypeKind kind = typeAttr.typekind switch {
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_ENUM      => LibraryTypeKind.Enum,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_DISPATCH  => LibraryTypeKind.DispatchInterface,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_INTERFACE => LibraryTypeKind.Interface,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_COCLASS   => LibraryTypeKind.Class,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_MODULE    => LibraryTypeKind.Module,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_ALIAS     => LibraryTypeKind.Alias,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_RECORD    => LibraryTypeKind.Struct,
                System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_UNION     => LibraryTypeKind.Struct,
                _ => (LibraryTypeKind)(-1),
            };

            if ((int)kind == -1) return null;

            List<LibraryMemberModel>    members    = [];
            List<LibraryEnumValueModel> enumValues = [];
            string? aliasedType = null;

            if (kind == LibraryTypeKind.Alias) {
                aliasedType = ResolveType(typeLib, typeInfo, typeAttr.tdescAlias, discoveredDeps);
            }
            else if (kind == LibraryTypeKind.Enum) {
                enumValues = InspectEnumValues(typeInfo, typeAttr.cVars);
            }
            else if (kind == LibraryTypeKind.Class) {
                // For a coclass, follow the default source interface
                members = InspectCoclassMembers(typeLib, typeInfo, typeAttr, discoveredDeps);
            }
            else if (kind == LibraryTypeKind.Struct) {
                members = InspectStructFields(typeLib, typeInfo, typeAttr.cVars, discoveredDeps);
            }
            else {
                members = InspectFunctions(typeLib, typeInfo, typeAttr.cFuncs, discoveredDeps);
            }

            return new LibraryTypeModel(typeName, kind, members, enumValues, aliasedType);
        }
        catch (Exception ex) {
            throw new InvalidOperationException(
                $"Failed to inspect type '{typeName}' (index {index})",
                ex);
        }
        finally {
            if (pTypeAttr != IntPtr.Zero) {
                typeInfo.ReleaseTypeAttr(pTypeAttr);
            }

            Marshal.ReleaseComObject(typeInfo);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Enum values
    // ──────────────────────────────────────────────────────────────────────

    static List<LibraryEnumValueModel> InspectEnumValues(
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo, short cVars)
    {
        var values = new List<LibraryEnumValueModel>(cVars);

        for (int i = 0; i < cVars; i++) {
            typeInfo.GetVarDesc(i, out IntPtr pVarDesc);
            if (pVarDesc == IntPtr.Zero) continue;

            try {
                var varDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.VARDESC>(pVarDesc);
                if (varDesc.varkind != System.Runtime.InteropServices.ComTypes.VARKIND.VAR_CONST) {
                    continue;
                }

                typeInfo.GetDocumentation(varDesc.memid, out string memberName, out _, out _, out _);
                if (string.IsNullOrWhiteSpace(memberName)) continue;

                long value = 0;
                if (varDesc.desc.lpvarValue != IntPtr.Zero) {
                    try {
                        var variant = Marshal.GetObjectForNativeVariant(varDesc.desc.lpvarValue);
                        value = Convert.ToInt64(variant);
                    }
                    catch { /* skip value extraction */ }
                }

                values.Add(new LibraryEnumValueModel(memberName, value));
            }
            catch { /* skip this entry */ }
            finally {
                typeInfo.ReleaseVarDesc(pVarDesc);
            }
        }

        return values;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Struct / union fields (TKIND_RECORD / TKIND_UNION)
    // ──────────────────────────────────────────────────────────────────────

    static List<LibraryMemberModel> InspectStructFields(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        short cVars,
        HashSet<DiscoveredDependency> discoveredDeps)
    {
        var fields = new List<LibraryMemberModel>(cVars);

        for (int i = 0; i < cVars; i++) {
            typeInfo.GetVarDesc(i, out IntPtr pVarDesc);
            if (pVarDesc == IntPtr.Zero) continue;

            try {
                var varDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.VARDESC>(pVarDesc);
                if (varDesc.varkind != System.Runtime.InteropServices.ComTypes.VARKIND.VAR_PERINSTANCE)
                    continue;

                typeInfo.GetDocumentation(varDesc.memid, out string fieldName, out _, out _, out _);
                if (string.IsNullOrWhiteSpace(fieldName)) continue;

                string fieldType = ResolveType(typeLib, typeInfo, varDesc.elemdescVar.tdesc, discoveredDeps);
                if (fieldType == "void") continue;

                fields.Add(new LibraryMemberModel(fieldName, LibraryMemberKind.Field, fieldType, []));
            }
            catch { /* skip this field */ }
            finally {
                typeInfo.ReleaseVarDesc(pVarDesc);
            }
        }

        return fields;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Coclass — follow the default dispatch interface
    // ──────────────────────────────────────────────────────────────────────

    static List<LibraryMemberModel> InspectCoclassMembers(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo coclassTypeInfo,
        System.Runtime.InteropServices.ComTypes.TYPEATTR typeAttr,
        HashSet<DiscoveredDependency> discoveredDeps)
    {
        const int IMPLTYPEFLAG_FDEFAULT = 0x1;
        const int IMPLTYPEFLAG_FSOURCE  = 0x2;

        for (int i = 0; i < typeAttr.cImplTypes; i++) {
            try {
                coclassTypeInfo.GetImplTypeFlags(i, out System.Runtime.InteropServices.ComTypes.IMPLTYPEFLAGS implFlags);
                int flags = (int)implFlags;
                if ((flags & IMPLTYPEFLAG_FDEFAULT) == 0) continue;
                if ((flags & IMPLTYPEFLAG_FSOURCE)  != 0) continue;  // skip event source

                coclassTypeInfo.GetRefTypeOfImplType(i, out int href);
                coclassTypeInfo.GetRefTypeInfo(href, out var implTypeInfo);
                if (implTypeInfo == null) continue;

                implTypeInfo.GetTypeAttr(out IntPtr pImplAttr);
                if (pImplAttr == IntPtr.Zero) {
                    Marshal.ReleaseComObject(implTypeInfo);
                    continue;
                }

                try {
                    var implAttr = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pImplAttr);
                    return InspectFunctions(typeLib, implTypeInfo, implAttr.cFuncs, discoveredDeps);
                }
                finally {
                    implTypeInfo.ReleaseTypeAttr(pImplAttr);
                    Marshal.ReleaseComObject(implTypeInfo);
                }
            }
            catch { /* try next interface */ }
        }

        return [];
    }

    // ──────────────────────────────────────────────────────────────────────
    // Methods and properties
    // ──────────────────────────────────────────────────────────────────────

    const short FUNCFLAG_FRESTRICTED = 0x1;
    const short FUNCFLAG_FHIDDEN     = 0x40;

    static List<LibraryMemberModel> InspectFunctions(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        short cFuncs,
        HashSet<DiscoveredDependency> discoveredDeps)
    {
        var members = new List<LibraryMemberModel>(cFuncs);

        for (int i = 0; i < cFuncs; i++) {
            typeInfo.GetFuncDesc(i, out IntPtr pFuncDesc);
            if (pFuncDesc == IntPtr.Zero) continue;

            try {
                var funcDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.FUNCDESC>(pFuncDesc);

                // Skip restricted (internal COM plumbing) functions
                if ((funcDesc.wFuncFlags & FUNCFLAG_FRESTRICTED) != 0) continue;

                typeInfo.GetDocumentation(funcDesc.memid, out string memberName, out _, out _, out _);
                if (string.IsNullOrWhiteSpace(memberName)) continue;

                var memberKind = funcDesc.invkind switch {
                    System.Runtime.InteropServices.ComTypes.INVOKEKIND.INVOKE_PROPERTYGET    => LibraryMemberKind.PropertyGet,
                    System.Runtime.InteropServices.ComTypes.INVOKEKIND.INVOKE_PROPERTYPUT    => LibraryMemberKind.PropertySet,
                    System.Runtime.InteropServices.ComTypes.INVOKEKIND.INVOKE_PROPERTYPUTREF => LibraryMemberKind.PropertySet,
                    _                                                                         => LibraryMemberKind.Method,
                };

                string returnType = ResolveType(typeLib, typeInfo, funcDesc.elemdescFunc.tdesc, discoveredDeps);

                // Get parameter names via GetNames (index 0 = function name, 1..n = params)
                int nameCount = funcDesc.cParams + 1;
                string[] names = new string[nameCount];
                typeInfo.GetNames(funcDesc.memid, names, nameCount, out int actualNames);

                var parameters = new List<LibraryParameterModel>(funcDesc.cParams);
                int elemDescSize = Marshal.SizeOf<System.Runtime.InteropServices.ComTypes.ELEMDESC>();

                for (int p = 0; p < funcDesc.cParams; p++) {
                    try {
                        var elemDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.ELEMDESC>(
                            IntPtr.Add(funcDesc.lprgelemdescParam, p * elemDescSize));

                        string paramName = (p + 1 < actualNames && names[p + 1] != null)
                            ? names[p + 1]
                            : $"param{p + 1}";

                        string paramType = ResolveType(typeLib, typeInfo, elemDesc.tdesc, discoveredDeps);

                        if (paramType == "void") {
                            continue;
                        }

                        bool isOptional = (elemDesc.desc.paramdesc.wParamFlags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FOPT) != 0;
                        bool isOut = (elemDesc.desc.paramdesc.wParamFlags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FOUT) != 0
                            && (elemDesc.desc.paramdesc.wParamFlags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FIN) == 0;

                        parameters.Add(new LibraryParameterModel(paramName, paramType, isOptional, isOut));
                    }
                    catch { /* skip parameter */ }
                }

                members.Add(new LibraryMemberModel(memberName, memberKind, returnType, parameters));
            }
            catch { /* skip this function */ }
            finally {
                typeInfo.ReleaseFuncDesc(pFuncDesc);
            }
        }

        return members;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type mapping
    // ──────────────────────────────────────────────────────────────────────

    static string ResolveType(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        System.Runtime.InteropServices.ComTypes.TYPEDESC typeDesc,
        HashSet<DiscoveredDependency> discoveredDeps)
    {
        var vt = (System.Runtime.InteropServices.VarEnum)typeDesc.vt;

        return vt switch {
            System.Runtime.InteropServices.VarEnum.VT_VOID      => "void",
            System.Runtime.InteropServices.VarEnum.VT_HRESULT   => "int",
            System.Runtime.InteropServices.VarEnum.VT_BOOL      => "bool",
            System.Runtime.InteropServices.VarEnum.VT_I1        => "sbyte",
            System.Runtime.InteropServices.VarEnum.VT_UI1       => "byte",
            System.Runtime.InteropServices.VarEnum.VT_I2        => "short",
            System.Runtime.InteropServices.VarEnum.VT_UI2       => "ushort",
            System.Runtime.InteropServices.VarEnum.VT_I4        => "int",
            System.Runtime.InteropServices.VarEnum.VT_UI4       => "uint",
            System.Runtime.InteropServices.VarEnum.VT_INT       => "int",
            System.Runtime.InteropServices.VarEnum.VT_UINT      => "uint",
            System.Runtime.InteropServices.VarEnum.VT_I8        => "long",
            System.Runtime.InteropServices.VarEnum.VT_UI8       => "ulong",
            System.Runtime.InteropServices.VarEnum.VT_R4        => "float",
            System.Runtime.InteropServices.VarEnum.VT_R8        => "double",
            System.Runtime.InteropServices.VarEnum.VT_CY        => "decimal",
            System.Runtime.InteropServices.VarEnum.VT_DECIMAL   => "decimal",
            System.Runtime.InteropServices.VarEnum.VT_DATE      => "System.DateTime",
            System.Runtime.InteropServices.VarEnum.VT_BSTR      => "string",
            System.Runtime.InteropServices.VarEnum.VT_LPSTR     => "string",
            System.Runtime.InteropServices.VarEnum.VT_LPWSTR    => "string",
            System.Runtime.InteropServices.VarEnum.VT_VARIANT   => "object",
            System.Runtime.InteropServices.VarEnum.VT_DISPATCH  => "object",
            System.Runtime.InteropServices.VarEnum.VT_UNKNOWN   => "object",
            System.Runtime.InteropServices.VarEnum.VT_ERROR     => "int",
            System.Runtime.InteropServices.VarEnum.VT_EMPTY     => "void",

            System.Runtime.InteropServices.VarEnum.VT_PTR when typeDesc.lpValue != IntPtr.Zero =>
                ResolveType(typeLib, typeInfo,
                    Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEDESC>(typeDesc.lpValue), discoveredDeps),

            System.Runtime.InteropServices.VarEnum.VT_SAFEARRAY when typeDesc.lpValue != IntPtr.Zero =>
                ResolveType(typeLib, typeInfo,
                    Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEDESC>(typeDesc.lpValue), discoveredDeps) + "[]",

            System.Runtime.InteropServices.VarEnum.VT_USERDEFINED =>
                ResolveUserDefinedType(typeLib, typeInfo, (int)(long)typeDesc.lpValue, discoveredDeps),

            _ => "object",
        };
    }

    static string ResolveUserDefinedType(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo, int hRefType,
        HashSet<DiscoveredDependency> discoveredDeps)
    {
        try {
            typeInfo.GetRefTypeInfo(hRefType, out var refTypeInfo);
            if (refTypeInfo == null) return "object";

            try {
                // Record the library that owns this referenced type as a discovered dependency,
                // and capture its safe namespace name for use in the fully-qualified return value.
                string? refLibNamespace = null;
                try {
                    refTypeInfo.GetContainingTypeLib(out var containingLib, out _);
                    if (containingLib != null) {
                        try {
                            containingLib.GetLibAttr(out IntPtr pLibAttr);
                            if (pLibAttr != IntPtr.Zero) {
                                try {
                                    var libAttr = Marshal.PtrToStructure<TLIBATTR>(pLibAttr);
                                    discoveredDeps.Add(new DiscoveredDependency(libAttr.guid, libAttr.wMajorVerNum, libAttr.wMinorVerNum));
                                }
                                finally {
                                    containingLib.ReleaseTLibAttr(pLibAttr);
                                }
                            }
                            containingLib.GetDocumentation(-1, out string libName, out _, out _, out _);
                            if (!string.IsNullOrWhiteSpace(libName))
                                refLibNamespace = ReferenceNaming.MakeSafeName(libName);
                        }
                        finally {
                            Marshal.ReleaseComObject(containingLib);
                        }
                    }
                }
                catch { /* best-effort; dependency collection must not break resolution */ }

                // If the referenced type is itself an alias, follow the chain recursively.
                // Alias chains resolve to a C# primitive keyword — no namespace prefix needed.
                refTypeInfo.GetTypeAttr(out IntPtr pRefAttr);
                if (pRefAttr != IntPtr.Zero) {
                    try {
                        var refAttr = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pRefAttr);
                        if (refAttr.typekind == System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_ALIAS) {
                            return ResolveType(typeLib, refTypeInfo, refAttr.tdescAlias, discoveredDeps);
                        }
                    }
                    finally {
                        refTypeInfo.ReleaseTypeAttr(pRefAttr);
                    }
                }

                refTypeInfo.GetDocumentation(-1, out string name, out _, out _, out _);
                if (string.IsNullOrWhiteSpace(name)) return "object";

                // Preserve the canonical casing returned by GetDocumentation before calling
                // FindName — COM's ITypeLib::FindName receives the name as a mutable WCHAR buffer
                // and writes back the matched casing in place, which mutates the interned .NET
                // string object. Create a fresh independent string to prevent this corruption.
                string canonicalName = new string(name.AsSpan());

                // Many VB6 controls re-declare shared types (e.g. MousePointerConstants) locally in
                // their own TLB even though GetContainingTypeLib() returns the original owner library
                // (e.g. VBRUN).  If the type name also appears directly in the library currently being
                // inspected, prefer that local namespace — the local copy will have a stub generated,
                // whereas the foreign library may not.
                string? ns = refLibNamespace;
                try {
                    short found = 1;
                    var localInfos  = new System.Runtime.InteropServices.ComTypes.ITypeInfo[1];
                    var localMemIds = new int[1];
                    typeLib.FindName(name, 0, localInfos, localMemIds, ref found);
                    if (found > 0 && localInfos[0] != null) {
                        try {
                            localInfos[0].GetDocumentation(-1, out string localTypeName, out _, out _, out _);
                            if (string.Equals(localTypeName, canonicalName, StringComparison.OrdinalIgnoreCase)) {
                                typeLib.GetDocumentation(-1, out string localLibName, out _, out _, out _);
                                if (!string.IsNullOrWhiteSpace(localLibName))
                                    ns = ReferenceNaming.MakeSafeName(localLibName);
                            }
                        }
                        finally {
                            Marshal.ReleaseComObject(localInfos[0]);
                        }
                    }
                }
                catch { /* best-effort; falls back to containing library namespace */ }

                return ns != null ? $"{ns}.{canonicalName}" : canonicalName;
            }
            finally {
                Marshal.ReleaseComObject(refTypeInfo);
            }
        }
        catch {
            return "object";
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    // MakeSafeName is exposed via ReferenceNaming.MakeSafeName (no platform dependency).
    public static string MakeSafeName(string raw) => ReferenceNaming.MakeSafeName(raw);

    static string TryGetTypeName(System.Runtime.InteropServices.ComTypes.ITypeLib typeLib, int index)
    {
        try {
            typeLib.GetDocumentation(index, out string typeName, out _, out _, out _);
            return string.IsNullOrWhiteSpace(typeName) ? "<unknown>" : typeName;
        }
        catch {
            return "<unknown>";
        }
    }

    static void LogInspectionFailure(VisualBasicProjectReference reference, string path, string stage, Exception ex)
    {
        var comEx = ex as COMException ?? ex.InnerException as COMException;
        if (comEx != null) {
            Log.Default.Warning(ex,
                "TypeLibraryInspector: {stage} failed for {path} ({description}, {guid}, v{major}.{minor}, lcid {lcid}) with COM HRESULT 0x{hresult:X8}",
                stage,
                path,
                reference.Description,
                reference.Guid,
                reference.MajorVersion,
                reference.MinorVersion,
                reference.Lcid,
                comEx.HResult);
            return;
        }

        Log.Default.Warning(ex,
            "TypeLibraryInspector: {stage} failed for {path} ({description}, {guid}, v{major}.{minor}, lcid {lcid})",
            stage,
            path,
            reference.Description,
            reference.Guid,
            reference.MajorVersion,
            reference.MinorVersion,
            reference.Lcid);
    }

    static string DescribeLoadTypeLibFailure(int hr) => hr switch {
        unchecked((int)0x80028019) => "TYPE_E_UNSUPFORMAT (invalid or unsupported type library format)",
        unchecked((int)0x80029C4A) => "TYPE_E_CANTLOADLIBRARY (library or a dependency could not be loaded)",
        unchecked((int)0x80070002) => "ERROR_FILE_NOT_FOUND",
        unchecked((int)0x8007007E) => "ERROR_MOD_NOT_FOUND (dependent module missing)",
        unchecked((int)0x8007000B) => "ERROR_BAD_FORMAT (architecture mismatch or invalid binary)",
        _ => "unknown",
    };

    static bool TryLoadTypeLibWithFallback(
        VisualBasicProjectReference reference,
        string primaryPath,
        out System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        out string loadedPath,
        out int lastHr)
    {
        typeLib = null!;
        loadedPath = primaryPath;
        lastHr = unchecked((int)0x80004005);

        foreach (var candidate in GetTypeLibLoadCandidates(reference, primaryPath)) {
            int hr = LoadTypeLib(candidate, out var candidateTypeLib);
            if (hr == 0 && candidateTypeLib != null) {
                typeLib = candidateTypeLib;
                loadedPath = candidate;
                lastHr = hr;
                return true;
            }

            lastHr = hr;
            Log.Default.Warning("TypeLibraryInspector: LoadTypeLib({path}) returned HRESULT 0x{hr:X8} ({reason})",
                candidate,
                hr,
                DescribeLoadTypeLibFailure(hr));
        }

        return false;
    }

    static IEnumerable<string> GetTypeLibLoadCandidates(VisualBasicProjectReference reference, string primaryPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldReturnList = new List<string>();

        void AddCandidate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!VisualBasicProject.IsTypeLibPath(path)) return;
            if (!seen.Add(path)) return;
            yieldReturnList.Add(path);
        }

        AddCandidate(primaryPath);

        foreach (var path in EnumerateRegisteredTypeLibPaths(reference.Guid, reference.MajorVersion, reference.MinorVersion, reference.Lcid)) {
            AddCandidate(path);
        }

        foreach (var path in EnumerateSiblingTypeLibFiles(primaryPath)) {
            AddCandidate(path);
        }

        foreach (var path in EnumerateTypeLibRegistryPathsByStem(primaryPath)) {
            AddCandidate(path);
        }

        return yieldReturnList;
    }

    static IEnumerable<string> EnumerateRegisteredTypeLibPaths(Guid guid, int major, int minor, int lcid)
    {
        using var root = Registry.ClassesRoot.OpenSubKey($@"TypeLib\{{{guid}}}");
        if (root == null) yield break;

        string versionKey = $"{major}.{minor}";

        foreach (var version in OrderKeysPrefer(root.GetSubKeyNames(), versionKey)) {
            using var versionKeyRef = root.OpenSubKey(version);
            if (versionKeyRef == null) continue;

            foreach (var lcidKey in OrderKeysPrefer(versionKeyRef.GetSubKeyNames(), lcid.ToString(), "0")) {
                using var lcidRef = versionKeyRef.OpenSubKey(lcidKey);
                if (lcidRef == null) continue;

                foreach (var arch in OrderKeysPrefer(lcidRef.GetSubKeyNames(), "win64", "win32")) {
                    using var archRef = lcidRef.OpenSubKey(arch);
                    var path = archRef?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(path)) {
                        yield return path;
                    }
                }
            }
        }
    }

    static IEnumerable<string> EnumerateSiblingTypeLibFiles(string primaryPath)
    {
        string? directory = Path.GetDirectoryName(primaryPath);
        string stem = Path.GetFileNameWithoutExtension(primaryPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem)) yield break;

        foreach (var ext in new[] { ".tlb", ".olb", ".oca", ".ocx", ".dll" }) {
            yield return Path.Combine(directory, stem + ext);
        }
    }

    static IEnumerable<string> EnumerateTypeLibRegistryPathsByStem(string primaryPath)
    {
        string stem = Path.GetFileNameWithoutExtension(primaryPath);
        if (string.IsNullOrWhiteSpace(stem)) yield break;

        foreach (var rootPath in new[] { @"TypeLib", @"Wow6432Node\TypeLib" }) {
            using var typeLibRoot = Registry.ClassesRoot.OpenSubKey(rootPath);
            if (typeLibRoot == null) continue;

            foreach (var guidKeyName in typeLibRoot.GetSubKeyNames()) {
                using var guidKey = typeLibRoot.OpenSubKey(guidKeyName);
                if (guidKey == null) continue;

                foreach (var versionKeyName in guidKey.GetSubKeyNames()) {
                    using var versionKey = guidKey.OpenSubKey(versionKeyName);
                    if (versionKey == null) continue;

                    foreach (var lcidKeyName in versionKey.GetSubKeyNames()) {
                        using var lcidKey = versionKey.OpenSubKey(lcidKeyName);
                        if (lcidKey == null) continue;

                        foreach (var archKeyName in lcidKey.GetSubKeyNames()) {
                            using var archKey = lcidKey.OpenSubKey(archKeyName);
                            var path = archKey?.GetValue(null) as string;
                            if (string.IsNullOrWhiteSpace(path)) continue;

                            string candidateStem = Path.GetFileNameWithoutExtension(path);
                            if (string.Equals(candidateStem, stem, StringComparison.OrdinalIgnoreCase)) {
                                yield return path;
                            }
                        }
                    }
                }
            }
        }
    }

    static IEnumerable<string> OrderKeysPrefer(IEnumerable<string> keys, params string[] preferred)
    {
        var remaining = new List<string>(keys);

        foreach (var key in preferred) {
            int index = remaining.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;

            yield return remaining[index];
            remaining.RemoveAt(index);
        }

        foreach (var key in remaining) {
            yield return key;
        }
    }
}
