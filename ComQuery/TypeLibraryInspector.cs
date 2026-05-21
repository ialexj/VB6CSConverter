#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using VB6Parser;

namespace ComQuery;

/// <summary>
/// Inspects COM type libraries using the Windows OLE Automation API and builds
/// <see cref="ComQueryLibrary"/> instances describing types, members, and dependencies.
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
    // Public entry points
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the type library at <paramref name="path"/> and returns a
    /// <see cref="ComQueryLibrary"/>, or <see langword="null"/> if the file
    /// cannot be loaded or parsed.
    /// </summary>
    public static ComQueryLibrary? Inspect(VisualBasicProjectReference reference, string path)
    {
        if (!TryLoadTypeLibWithFallback(reference.Guid, reference.MajorVersion, reference.MinorVersion, reference.Lcid, path,
                                        out var typeLib, out string loadedFromPath, out int hr)) {
            Log.Warning($"TypeLibraryInspector: unable to load type library for {path}; last HRESULT 0x{hr:X8} ({DescribeLoadTypeLibFailure(hr)})");
            return null;
        }

        if (!string.Equals(path, loadedFromPath, StringComparison.OrdinalIgnoreCase)) {
            Log.Information($"TypeLibraryInspector: using fallback type library path {loadedFromPath} for {path}");
        }

        try {
            return InspectTypeLib(typeLib, reference.Guid, reference.MajorVersion, reference.MinorVersion, reference.IsTransitive, loadedFromPath, reference.Description);
        }
        catch (Exception ex) {
            Log.Warning($"TypeLibraryInspector: InspectTypeLib failed for {loadedFromPath}", ex);
            return null;
        }
        finally {
            Marshal.ReleaseComObject(typeLib);
        }
    }

    /// <summary>
    /// Loads the type library identified by <paramref name="guid"/>, version, and
    /// <paramref name="path"/>, and returns a <see cref="ComQueryLibrary"/>, or
    /// <see langword="null"/> if the file cannot be loaded or parsed.
    /// </summary>
    public static ComQueryLibrary? Inspect(Guid guid, int major, int minor, string name, string path, bool isTransitive = false)
    {
        if (!TryLoadTypeLibWithFallback(guid, major, minor, 0, path,
                                        out var typeLib, out string loadedFromPath, out int hr)) {
            Log.Warning($"TypeLibraryInspector: unable to load type library for {path}; last HRESULT 0x{hr:X8} ({DescribeLoadTypeLibFailure(hr)})");
            return null;
        }

        if (!string.Equals(path, loadedFromPath, StringComparison.OrdinalIgnoreCase)) {
            Log.Information($"TypeLibraryInspector: using fallback type library path {loadedFromPath} for {path}");
        }

        try {
            return InspectTypeLib(typeLib, guid, major, minor, isTransitive, loadedFromPath, name);
        }
        catch (Exception ex) {
            Log.Warning($"TypeLibraryInspector: InspectTypeLib failed for {loadedFromPath}", ex);
            return null;
        }
        finally {
            Marshal.ReleaseComObject(typeLib);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type-library level
    // ──────────────────────────────────────────────────────────────────────

    static ComQueryLibrary InspectTypeLib(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        Guid guid, int major, int minor, bool isTransitive,
        string loadedFromPath, string fallbackName)
    {
        typeLib.GetDocumentation(-1, out string libName, out string libDoc, out _, out _);
        libName = string.IsNullOrWhiteSpace(libName) ? fallbackName : libName;
        string? libDescription = string.IsNullOrWhiteSpace(libDoc) ? null : libDoc;

        string safeName = ReferenceNaming.MakeSafeName(libName);

        int typeCount = typeLib.GetTypeInfoCount();
        var types     = new List<ComQueryType>(typeCount);
        var discoveredDeps = new HashSet<ComQueryDiscoveredDep>();

        for (int i = 0; i < typeCount; i++) {
            ComQueryType? typeModel;
            try {
                typeModel = InspectTypeInfo(typeLib, i, discoveredDeps);
            }
            catch (Exception ex) {
                string typeName = TryGetTypeName(typeLib, i);
                Log.Warning($"TypeLibraryInspector: skipping type index {i} ({typeName}) in library {libName} after inspection failure", ex);
                continue;
            }

            if (typeModel != null) {
                types.Add(typeModel);
            }
        }

        // Post-process: a DispatchInterface with 0 members (e.g. stdole.Font) often has a
        // corresponding vtable Interface that carries all the real members (e.g. stdole.IFont).
        // COM doesn't encode this relationship explicitly, but the naming convention is consistent:
        //   dispinterface "X"  ←→  interface "IX"
        // Add the vtable interface as a base so the generated C# stub exposes those members.
        var vtableInterfaceNames = types
            .Where(t => t.Kind == LibraryTypeKind.Interface)
            .ToLookup(t => t.Name, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < types.Count; i++) {
            var t = types[i];
            if (t.Kind != LibraryTypeKind.DispatchInterface) continue;
            if (t.Members != null && t.Members.Count > 0) continue;

            string candidate = "I" + t.Name;
            if (!vtableInterfaceNames.Contains(candidate)) continue;

            var existing = t.ImplementedInterfaces ?? (IReadOnlyList<string>)[];
            if (existing.Any(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase))) continue;

            types[i] = t with { ImplementedInterfaces = [.. existing, candidate] };
        }

        IReadOnlyList<ComQueryDiscoveredDep>? deps = discoveredDeps.Count > 0 ? [.. discoveredDeps] : null;

        return new ComQueryLibrary(
            Name: libName,
            SafeName: safeName,
            Guid: guid,
            Major: major,
            Minor: minor,
            Path: loadedFromPath,
            IsTransitive: isTransitive,
            Types: types.Count > 0 ? types : null,
            DiscoveredDependencies: deps,
            Description: libDescription);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type-info level
    // ──────────────────────────────────────────────────────────────────────

    static ComQueryType? InspectTypeInfo(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        int index,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
    {
        typeLib.GetTypeInfo(index, out var typeInfo);
        if (typeInfo == null) return null;

        string typeName = "<unknown>";
        IntPtr pTypeAttr = IntPtr.Zero;

        try {
            typeInfo.GetDocumentation(-1, out typeName, out string typeDoc, out _, out _);
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            string? typeDescription = string.IsNullOrWhiteSpace(typeDoc) ? null : typeDoc;

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

            List<ComQueryMember>    members    = [];
            List<ComQueryEnumVal>   enumValues = [];
            string? aliasedType = null;
            List<string> implementedInterfaces = [];

            if (kind == LibraryTypeKind.Alias) {
                aliasedType = ResolveType(typeLib, typeInfo, typeAttr.tdescAlias, discoveredDeps);
            }
            else if (kind == LibraryTypeKind.Enum) {
                enumValues = InspectEnumValues(typeInfo, typeAttr.cVars);
            }
            else if (kind == LibraryTypeKind.Class) {
                var coclassInfo = InspectCoclassMembers(typeLib, typeInfo, typeAttr, discoveredDeps);
                members = coclassInfo.Members;
                implementedInterfaces = coclassInfo.ImplementedInterfaces;
                if (coclassInfo.HasNewEnum)
                    implementedInterfaces.Insert(0, "System.Collections.IEnumerable");
            }
            else if (kind == LibraryTypeKind.Struct) {
                members = InspectStructFields(typeLib, typeInfo, typeAttr.cVars, discoveredDeps);
            }
            else {
                var interfaceInfo = InspectInterfaceMembers(typeLib, typeInfo, typeAttr, discoveredDeps);
                members = interfaceInfo.Members;
                implementedInterfaces = interfaceInfo.BaseInterfaces;
                if (interfaceInfo.HasNewEnum)
                    implementedInterfaces.Insert(0, "System.Collections.IEnumerable");
            }

            return new ComQueryType(
                Name: typeName,
                Kind: kind,
                Members: members.Count > 0 ? members : null,
                EnumValues: enumValues.Count > 0 ? enumValues : null,
                AliasedType: aliasedType,
                ImplementedInterfaces: implementedInterfaces.Count > 0 ? implementedInterfaces : null,
                Description: typeDescription);
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

    static List<ComQueryEnumVal> InspectEnumValues(
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo, short cVars)
    {
        var values = new List<ComQueryEnumVal>(cVars);

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

                values.Add(new ComQueryEnumVal(memberName, value));
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

    static List<ComQueryMember> InspectStructFields(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        short cVars,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
    {
        var fields = new List<ComQueryMember>(cVars);

        for (int i = 0; i < cVars; i++) {
            typeInfo.GetVarDesc(i, out IntPtr pVarDesc);
            if (pVarDesc == IntPtr.Zero) continue;

            try {
                var varDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.VARDESC>(pVarDesc);
                if (varDesc.varkind != System.Runtime.InteropServices.ComTypes.VARKIND.VAR_PERINSTANCE)
                    continue;

                typeInfo.GetDocumentation(varDesc.memid, out string fieldName, out string fieldDoc, out _, out _);
                if (string.IsNullOrWhiteSpace(fieldName)) continue;

                string fieldType = ResolveType(typeLib, typeInfo, varDesc.elemdescVar.tdesc, discoveredDeps);
                if (fieldType == "void") continue;

                string? fieldDescription = string.IsNullOrWhiteSpace(fieldDoc) ? null : fieldDoc;
                fields.Add(new ComQueryMember(fieldName, LibraryMemberKind.Field, fieldType, [], DispId: varDesc.memid, Description: fieldDescription));
            }
            catch { /* skip this field */ }
            finally {
                typeInfo.ReleaseVarDesc(pVarDesc);
            }
        }

        return fields;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Coclass / interface graph walking
    // ──────────────────────────────────────────────────────────────────────

    const int IMPLTYPEFLAG_FSOURCE     = 0x2;
    const int IMPLTYPEFLAG_FRESTRICTED = 0x4;

    static (List<ComQueryMember> Members, List<string> ImplementedInterfaces, bool HasNewEnum) InspectCoclassMembers(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo coclassTypeInfo,
        System.Runtime.InteropServices.ComTypes.TYPEATTR typeAttr,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
    {
        var members = new List<ComQueryMember>();
        var memberSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var implementedInterfaces = new List<string>();
        var implementedInterfaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedInterfaces = new HashSet<Guid>();
        bool hasNewEnum = false;

        for (int i = 0; i < typeAttr.cImplTypes; i++) {
            try {
                coclassTypeInfo.GetImplTypeFlags(i, out System.Runtime.InteropServices.ComTypes.IMPLTYPEFLAGS implFlags);
                int flags = (int)implFlags;
                if ((flags & IMPLTYPEFLAG_FSOURCE) != 0) continue;

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

                    if (implAttr.typekind != System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_INTERFACE
                        && implAttr.typekind != System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_DISPATCH) {
                        continue;
                    }

                    implTypeInfo.GetDocumentation(-1, out string interfaceName, out _, out _, out _);
                    if (!string.IsNullOrWhiteSpace(interfaceName) && implementedInterfaceNames.Add(interfaceName)) {
                        implementedInterfaces.Add(interfaceName);
                    }

                    CollectInterfaceMembersRecursive(
                        typeLib,
                        implTypeInfo,
                        implAttr,
                        discoveredDeps,
                        visitedInterfaces,
                        members,
                        memberSignatures,
                        includeBaseInterfaces: false,
                        baseInterfaces: null,
                        ref hasNewEnum);
                }
                finally {
                    implTypeInfo.ReleaseTypeAttr(pImplAttr);
                    Marshal.ReleaseComObject(implTypeInfo);
                }
            }
            catch { /* try next interface */ }
        }

        return (members, implementedInterfaces, hasNewEnum);
    }

    static (List<ComQueryMember> Members, List<string> BaseInterfaces, bool HasNewEnum) InspectInterfaceMembers(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        System.Runtime.InteropServices.ComTypes.TYPEATTR typeAttr,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
    {
        var members = new List<ComQueryMember>();
        var memberSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseInterfaces = new List<string>();
        var baseInterfaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedInterfaces = new HashSet<Guid>();
        bool hasNewEnum = false;

        CollectInterfaceMembersRecursive(
            typeLib,
            typeInfo,
            typeAttr,
            discoveredDeps,
            visitedInterfaces,
            members,
            memberSignatures,
            includeBaseInterfaces: true,
            baseInterfaces: (baseInterfaces, baseInterfaceNames),
            ref hasNewEnum);

        return (members, baseInterfaces, hasNewEnum);
    }

    static void CollectInterfaceMembersRecursive(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        System.Runtime.InteropServices.ComTypes.TYPEATTR typeAttr,
        HashSet<ComQueryDiscoveredDep> discoveredDeps,
        HashSet<Guid> visitedInterfaces,
        List<ComQueryMember> members,
        HashSet<string> memberSignatures,
        bool includeBaseInterfaces,
        (List<string> Names, HashSet<string> Seen)? baseInterfaces,
        ref bool hasNewEnum)
    {
        if (!visitedInterfaces.Add(typeAttr.guid)) {
            return;
        }

        var (newMembers, newEnumFound) = InspectFunctions(typeLib, typeInfo, typeAttr.cFuncs, typeAttr.typekind, discoveredDeps);
        if (newEnumFound) hasNewEnum = true;
        foreach (var member in newMembers) {
            if (memberSignatures.Add(GetMemberSignature(member))) {
                members.Add(member);
            }
        }

        // Pure dispinterfaces may describe their properties as VAR_DISPATCH VARDESCs
        // (the "properties:" section in ODL/IDL) rather than as INVOKE_PROPERTYGET/PUT
        // FUNCDESCs.  Read cVars so those properties are not silently dropped.
        if (typeAttr.typekind == System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_DISPATCH
            && typeAttr.cVars > 0) {
            foreach (var member in InspectDispatchVarProperties(typeLib, typeInfo, typeAttr.cVars, discoveredDeps)) {
                if (memberSignatures.Add(GetMemberSignature(member))) {
                    members.Add(member);
                }
            }
        }

        for (int i = 0; i < typeAttr.cImplTypes; i++) {
            try {
                typeInfo.GetImplTypeFlags(i, out System.Runtime.InteropServices.ComTypes.IMPLTYPEFLAGS implFlags);
                int flags = (int)implFlags;

                if ((flags & IMPLTYPEFLAG_FSOURCE) != 0) continue;

                typeInfo.GetRefTypeOfImplType(i, out int href);
                typeInfo.GetRefTypeInfo(href, out var parentTypeInfo);
                if (parentTypeInfo == null) continue;

                parentTypeInfo.GetTypeAttr(out IntPtr pParentAttr);
                if (pParentAttr == IntPtr.Zero) {
                    Marshal.ReleaseComObject(parentTypeInfo);
                    continue;
                }

                try {
                    var parentAttr = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pParentAttr);
                    if (parentAttr.typekind != System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_INTERFACE
                        && parentAttr.typekind != System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_DISPATCH) {
                        continue;
                    }

                    if (includeBaseInterfaces && baseInterfaces != null) {
                        parentTypeInfo.GetDocumentation(-1, out string parentName, out _, out _, out _);
                        if (!string.IsNullOrWhiteSpace(parentName) && baseInterfaces.Value.Seen.Add(parentName)) {
                            baseInterfaces.Value.Names.Add(parentName);
                        }
                    }

                    CollectInterfaceMembersRecursive(
                        typeLib,
                        parentTypeInfo,
                        parentAttr,
                        discoveredDeps,
                        visitedInterfaces,
                        members,
                        memberSignatures,
                        includeBaseInterfaces,
                        baseInterfaces,
                        ref hasNewEnum);
                }
                finally {
                    parentTypeInfo.ReleaseTypeAttr(pParentAttr);
                    Marshal.ReleaseComObject(parentTypeInfo);
                }
            }
            catch {
                // Best effort: inheritance inspection should not abort the type.
            }
        }
    }

    static string GetMemberSignature(ComQueryMember member)
    {
        string parameterTypes = string.Join(",", member.Parameters.Select(p => p.Type));
        return $"{member.Kind}:{member.Name}({parameterTypes})=>{member.ReturnType}";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Dispatch interface VAR_DISPATCH properties
    // ──────────────────────────────────────────────────────────────────────

    const short VARFLAG_FREADONLY = 0x1;

    static List<ComQueryMember> InspectDispatchVarProperties(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        short cVars,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
    {
        var members = new List<ComQueryMember>(cVars * 2);

        for (int i = 0; i < cVars; i++) {
            typeInfo.GetVarDesc(i, out IntPtr pVarDesc);
            if (pVarDesc == IntPtr.Zero) continue;

            try {
                var varDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.VARDESC>(pVarDesc);
                if (varDesc.varkind != System.Runtime.InteropServices.ComTypes.VARKIND.VAR_DISPATCH) continue;

                typeInfo.GetDocumentation(varDesc.memid, out string memberName, out string memberDoc, out _, out _);
                if (string.IsNullOrWhiteSpace(memberName)) continue;

                string propType = ResolveType(typeLib, typeInfo, varDesc.elemdescVar.tdesc, discoveredDeps);
                if (propType == "void") propType = "object";

                string? memberDescription = string.IsNullOrWhiteSpace(memberDoc) ? null : memberDoc;

                members.Add(new ComQueryMember(memberName, LibraryMemberKind.PropertyGet, propType, [], DispId: varDesc.memid, Description: memberDescription));

                bool isReadOnly = (varDesc.wVarFlags & VARFLAG_FREADONLY) != 0;
                if (!isReadOnly) {
                    members.Add(new ComQueryMember(memberName, LibraryMemberKind.PropertySet, "void",
                        [new ComQueryParam("value", propType, false, false)], DispId: varDesc.memid, Description: memberDescription));
                }
            }
            catch { /* skip this property */ }
            finally {
                typeInfo.ReleaseVarDesc(pVarDesc);
            }
        }

        return members;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Methods and properties
    // ──────────────────────────────────────────────────────────────────────

    const short FUNCFLAG_FRESTRICTED = 0x1;
    const short FUNCFLAG_FHIDDEN     = 0x40;
    const int   DISPID_NEWENUM       = -4;    // IEnumVARIANT enumerator factory

    static (List<ComQueryMember> Members, bool HasNewEnum) InspectFunctions(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        short cFuncs,
        System.Runtime.InteropServices.ComTypes.TYPEKIND typekind,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
    {
        var members = new List<ComQueryMember>(cFuncs);
        bool hasNewEnum = false;

        for (int i = 0; i < cFuncs; i++) {
            typeInfo.GetFuncDesc(i, out IntPtr pFuncDesc);
            if (pFuncDesc == IntPtr.Zero) continue;

            try {
                var funcDesc = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.FUNCDESC>(pFuncDesc);

                // Skip restricted functions only for vtable interfaces (TKIND_INTERFACE), where
                // FUNCFLAG_FRESTRICTED marks the inherited IUnknown/IDispatch plumbing slots.
                // For dispatch interfaces (TKIND_DISPATCH) these slots are NOT in cFuncs, so
                // FRESTRICTED there marks legitimately hidden but callable properties (e.g. ClientHeight).
                if (typekind == System.Runtime.InteropServices.ComTypes.TYPEKIND.TKIND_INTERFACE
                    && (funcDesc.wFuncFlags & FUNCFLAG_FRESTRICTED) != 0) continue;

                typeInfo.GetDocumentation(funcDesc.memid, out string memberName, out string memberDoc, out _, out _);
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

                var parameters = new List<ComQueryParam>(funcDesc.cParams);
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

                        var flags = elemDesc.desc.paramdesc.wParamFlags;
                        bool isRetVal = (flags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FRETVAL) != 0;

                        // Dual-interface members commonly expose HRESULT as the COM return type
                        // and place the real value in a [retval] parameter.
                        if (isRetVal) {
                            returnType = paramType;
                            continue;
                        }

                        bool isOptional = (flags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FOPT) != 0;
                        bool isOut = (flags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FOUT) != 0
                            && (flags
                            & System.Runtime.InteropServices.ComTypes.PARAMFLAG.PARAMFLAG_FIN) == 0;

                        parameters.Add(new ComQueryParam(paramName, paramType, isOptional, isOut));
                    }
                    catch { /* skip parameter */ }
                }

                // DISPID_NEWENUM (-4): suppress _NewEnum and substitute GetEnumerator so that
                // generated stubs can implement System.Collections.IEnumerable.
                if (funcDesc.memid == DISPID_NEWENUM) {
                    members.Add(new ComQueryMember("GetEnumerator", LibraryMemberKind.Method, "System.Collections.IEnumerator", [], DispId: DISPID_NEWENUM));
                    hasNewEnum = true;
                    continue;
                }

                bool isDefault = funcDesc.memid == 0
                    && (memberKind == LibraryMemberKind.PropertyGet || memberKind == LibraryMemberKind.PropertySet);

                string? memberDescription = string.IsNullOrWhiteSpace(memberDoc) ? null : memberDoc;

                members.Add(new ComQueryMember(memberName, memberKind, returnType, parameters, isDefault, DispId: funcDesc.memid, Description: memberDescription));
            }
            catch { /* skip this function */ }
            finally {
                typeInfo.ReleaseFuncDesc(pFuncDesc);
            }
        }

        return (members, hasNewEnum);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Type mapping
    // ──────────────────────────────────────────────────────────────────────

    static string ResolveType(
        System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo,
        System.Runtime.InteropServices.ComTypes.TYPEDESC typeDesc,
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
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
        HashSet<ComQueryDiscoveredDep> discoveredDeps)
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
                                    discoveredDeps.Add(new ComQueryDiscoveredDep(libAttr.guid, libAttr.wMajorVerNum, libAttr.wMinorVerNum));
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
                // string object. Allocate a truly independent copy to prevent this corruption.
                string canonicalName = string.Create(name.Length, name, (span, str) => str.AsSpan().CopyTo(span));

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

    static string DescribeLoadTypeLibFailure(int hr) => hr switch {
        unchecked((int)0x80028019) => "TYPE_E_UNSUPFORMAT (invalid or unsupported type library format)",
        unchecked((int)0x80029C4A) => "TYPE_E_CANTLOADLIBRARY (library or a dependency could not be loaded)",
        unchecked((int)0x80070002) => "ERROR_FILE_NOT_FOUND",
        unchecked((int)0x8007007E) => "ERROR_MOD_NOT_FOUND (dependent module missing)",
        unchecked((int)0x8007000B) => "ERROR_BAD_FORMAT (architecture mismatch or invalid binary)",
        _ => "unknown",
    };

    static bool TryLoadTypeLibWithFallback(
        Guid guid, int major, int minor, int lcid,
        string primaryPath,
        out System.Runtime.InteropServices.ComTypes.ITypeLib typeLib,
        out string loadedPath,
        out int lastHr)
    {
        typeLib = null!;
        loadedPath = primaryPath;
        lastHr = unchecked((int)0x80004005);

        foreach (var candidate in GetTypeLibLoadCandidates(guid, major, minor, lcid, primaryPath)) {
            int hr = LoadTypeLib(candidate, out var candidateTypeLib);
            if (hr == 0 && candidateTypeLib != null) {
                typeLib = candidateTypeLib;
                loadedPath = candidate;
                lastHr = hr;
                return true;
            }

            lastHr = hr;
            Log.Warning($"TypeLibraryInspector: LoadTypeLib({candidate}) returned HRESULT 0x{hr:X8} ({DescribeLoadTypeLibFailure(hr)})");
        }

        return false;
    }

    static IEnumerable<string> GetTypeLibLoadCandidates(Guid guid, int major, int minor, int lcid, string primaryPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        void AddCandidate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!VisualBasicProject.IsTypeLibPath(path)) return;
            if (!seen.Add(path)) return;
            candidates.Add(path);
        }

        AddCandidate(primaryPath);

        foreach (var path in EnumerateRegisteredTypeLibPaths(guid, major, minor, lcid)) {
            AddCandidate(path);
        }

        foreach (var path in EnumerateSiblingTypeLibFiles(primaryPath)) {
            AddCandidate(path);
        }

        foreach (var path in EnumerateTypeLibRegistryPathsByStem(primaryPath)) {
            AddCandidate(path);
        }

        return candidates;
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
