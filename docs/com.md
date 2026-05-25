# COM Type Libraries and Reference Stub Generation

## Motivation

One of the first issues that one faces when trying to do a VB6 conversion, are the referenced 
libraries. We need to somehow represent in the schema of the objects that the code is trying 
to use. 

One approach that the first version took was to try to reconstruct any objects and properties
that were flagged as missing by the compiler. Unfortunately VB6 is quite loose about types and
capitalization, etc. so this yields a result that's not very useful if we then want to use it
to improve overall correctness.

I've found it much more useful to inspect the actual references in the same way as VB6 does it, 
and generate object stubs with a clean "correct" reference schema. Then we can massage the rest
of the code to fit that correct schema. The stubs don't need to actually execute any code - it
just needs to compile.

It's also worth noting that .NET can make its own fully functional wrappers of COM objects, but
the schema very often won't match exactly with how the code is being called from VB6. 

COM References in .NET projects are also not supported by `dotnet build` and you need to invoke
the full VSBuild to have them.

Replacing stub usage with tlbexp-generated classes would be a good next step if the code 
needs to be brought into a working state.

## COM Overview

"COM" has become somewhat of a blanket term for a set of technologies (COM Proper, OLE, ActiveX),
that enable library discoverability and reflection in a pre-.NET world. It's a binary standard 
for interface and schema definitions, combined with a central component registry. 

In .NET, reflection is accomplished by putting metadata in the DLL itself, and the central 
registry idea has been mostly abandoned in favor of just packaging all the dependencies with 
each application, with the closest analogue being the GAC. The intent of this system is similar.

### COM, OLE, and ActiveX — terminology

These three terms are often used interchangeably in VB6 documentation but they refer to
distinct layers:

**COM** (Component Object Model) is the foundation.  It defines the binary interface standard:
every object exposes one or more interfaces, each interface is a vtable of function pointers,
and every interface ultimately derives from `IUnknown` (with `QueryInterface`, `AddRef`,
`Release` methods to get schema, create or destroy references).

COM Interfaces are implemented by *coclasses*, which are essentially just what classes are in .NET. 
Like in .NET, interfaces, classes and members can all have attributes that have different meanings.

A **COM server** is the binary — a DLL or EXE — that hosts one or more coclass implementations.
An *in-process server* (DLL) is loaded directly into the caller's address space, and the DLL's 
exported `DllGetClassObject` function is called to obtain a class factory. An *out-of-process server* (EXE)
runs in a separate process and calls are proxied via COM marshalling.

For VB6 controls and libraries, in-process DLL/OCX servers are by far the common case.

**OLE** (Object Linking and Embedding) is a set of higher-level services built on top of COM.
The original OLE was a compound-document technology (embed a spreadsheet inside a Word
document), but the term expanded to cover drag-and-drop, the clipboard, structured storage,
and **OLE Automation**.  OLE Automation adds the `IDispatch` interface, which allows late-bound,
name-based method invocation (the mechanism VBScript and VBA use to call methods without knowing 
the vtable layout at compile time).

**Type libraries** exist solely to describe Automation-compatible objects; `oleaut32.dll` is
the runtime that loads and walks them.

Types can implement either `IUnknown`, `IDispatch` or both.

**ActiveX** is a 1996 rebranding of *OLE Controls* (previously called OCX controls).  An
ActiveX control is a COM coclass that implements a specific cluster of OLE interfaces
(`IViewObject`, `IOleObject`, `IConnectionPointContainer`, …) so that a container — such as
the VB6 form designer — can host it visually, receive events from it, and persist its
properties.

From a type-library perspective, an ActiveX control is distinguished by a `TYPEFLAG_FCONTROL`
attribute on its coclass entry, although VB will let you use components that don't have the flag
set, and libraries often don't. For our purposes, it's easier to assume that it's a control 
if it comes from an `.ocx` file. This becomes important, as the VB6 designer will provide designer 
properties that aren't part of the library schema, but which are treated in the same way.

### COM registration

COM components announce their existence to the OS by writing entries into the Windows registry.
Registration/unregistration is generally performed by running `regsvr32.exe` against the DLL/OCX,
which calls the binary's `DllRegisterServer`/`DllUnregisterServer` functions, which are expected to 
be well behaved and write/remove the correct entries from the registry. But it's also possible to
just write the entries yourself, which many installers do for legacy libraries.

## COM References

A COM reference is identified by five fields that appear in the `.vbp` file, in the
registry, and in the type library's own metadata. They are not always consistent with each
other.

#### GUID(s)

COM uses GUIDs to identify several different things:

| Name | Also called | Registry home | Identifies |
|---|---|---|---|
| **LIBID** | type library GUID | `HKCR\TypeLib\{guid}` | A type library (the schema) |
| **CLSID** | class ID | `HKCR\CLSID\{clsid}` | A coclass implementation (which DLL/EXE to load) |
| **IID** | interface ID | `HKCR\Interface\{iid}` | An interface (for proxy/stub lookup) |

In common use, when one refers to a "COM GUID", it's usually the `CLSID`.

A VB6 reference uses the `LIBID`. This is the primary, stable identity for a type library.
It never changes between versions and is the key used for all registry lookups.
In the VBP it appears in braces: `{00020430-0000-0000-C000-000000000046}`.

#### Version (major.minor)

Version is a two-part integer pair stored in the registry as a `major.minor` subkey (e.g.
`2.0`) and in `TLIBATTR.wMajorVerNum` / `wMinorVerNum`.  It identifies a specific release of
a library.

Multiple versions of the same GUID can be registered simultaneously.  The VBP records the
exact version the original developer compiled against.  The registry lookup prefers that exact
version but falls back to whatever version is registered when the declared version is absent —
this handles cases where the machine has a newer revision installed.

#### LCID (locale ID)

The LCID is a Windows locale identifier.  Most type libraries are registered under LCID `0`
(language-neutral) and this value appears in most VBP lines.  A small number of libraries
(some VB runtime sub-libraries) are registered only under a language-specific LCID such as
`9` (English) with no neutral entry.  The registry lookup iterates the declared LCID first,
then `0`, then any remaining numeric LCID.

#### Name / description

A type library has two distinct name fields that are easily confused:

| Field | Source | Typical content |
|---|---|---|
| **VBP description** | The last `#`-delimited field of the `Reference=` line | `"OLE Automation"`, `"Microsoft Scripting Runtime"` |
| **Library name** | `ITypeLib::GetDocumentation(-1)` | `"stdole"`, `"Scripting"` |

The VBP description is a human-readable label chosen by the original developer (or copied
from the VB6 IDE).  The library name returned by `GetDocumentation` is the authoritative name
used to form the C# namespace for generated stubs.  The two often differ: for example, the
VBP description for stdole2 is `"OLE Automation"` while `GetDocumentation` returns `"stdole"`.

For `Object=` (ActiveX control) lines the description field is absent; the filename
(`COMDLG32.OCX`) is used as a fallback description.

### Kind and declared path

Two VBP line formats declare references:

```text
Reference=*\G{GUID}#major.minor#lcid#path#description
Object={GUID}#major.minor#lcid; filename.ocx
```

`Reference=` lines are type-library references (`ProjectReferenceKind.TypeLibrary`); they
carry an explicit file path.  `Object=` lines are ActiveX control references
(`ProjectReferenceKind.ActiveX`); the path is just a bare filename (no directory) that is
resolved via the registered path for the GUID.

The declared path in a `Reference=` line is often stale — it encodes the original developer's
machine path and will typically not exist on another machine.  It is tried first, then
discarded in favour of the registry-resolved path when the file is not found.

## Locating Libraries

### Registry

Libraries are registered in the registry under `HKEY_CLASSES_ROOT` (`HKCR`). This is a
merged view between the machine-wide `HKEY_LOCAL_MACHINE\Software\Classes`, and the per-user 
`HKEY_CURRENT_USER\Software\Classes`. 

```
HKCR\TypeLib\
  {LIBID}\
    {major}.{minor}\     ← version key; default value = library name
      {LCID}\            ← locale ID ("0" = neutral, "9" = English, …)
        win32\           ← default value = path to 32-bit .tlb/.dll/.ocx
        win64\           ← default value = path to 64-bit .tlb/.dll/.ocx

HKCR\CLSID\{clsid}`
  InprocServer32         ← path to in-process server (DLL)
  LocalServer32          ← path to out-of-process server (DLL)
```

Three subtrees are relevant here:

- **`HKCR\TypeLib\{LIBID}\{version}`** — maps a type-library GUID + version to the file that
  contains its type information.
- **`HKCR\CLSID\{CLSID}`** — maps a class GUID to the physical server that implements it.
- **`HKCR\Interface\{IID}`** — maps an interface IID to its proxy/stub CLSID, enabling
  marshalling across process or apartment boundaries.  Not relevant to stub generation.

#### 32-bit vs 64-bit registry hives

COM type libraries are registered under `HKCR\TypeLib\{GUID}\{version}\{lcid}\{arch}`.
Windows maintains two separate copies of this subtree:

| Process bitness | Registry view | Effective root |
|---|---|---|
| 64-bit | Native (default) | `HKLM\Software\Classes\TypeLib` |
| 32-bit | WOW64-redirected | `HKLM\Software\WOW6432Node\Classes\TypeLib` |

When `Registry.ClassesRoot` is opened from a 32-bit process the OS transparently redirects
it to the WOW6432Node hive, so the two hives are not separately visible from a single
process. This means:

- A 32-bit ActiveX control (`.ocx`) may be registered **only** in the WOW6432Node hive.
  A 64-bit process cannot see it via `Registry.ClassesRoot` and would report the library as
  unresolved.
- A 64-bit system type library (e.g. a 64-bit edition of a database driver) may be
  registered **only** in the native 64-bit hive, invisible to a 32-bit query.
- When the same GUID is registered in both hives the paths may differ (e.g. `SysWOW64` for
  the 32-bit copy, `System32` for the 64-bit copy), and the two copies may expose slightly
  different member signatures (see the x86/x64 merging section below).

The registry isn't the only hurdle - even if one forces the use of the 32-bit hive from 64 bit,
the calls to `ITypeInfo::GetRefTypeInfo` are also not consistent. See below in Known Issues.
ComQuery ships as **two separate executables** — `ComQuery32.exe` and `ComQuery64.exe` because of this.

#### Iteration Order

For explicit path-to-GUID lookups (`TryRegistryLookupInView`) both hive views are probed in
sequence: 64-bit first, 32-bit second, accepting the first result.

LCID subkeys are iterated in priority order: the reference's own LCID, then `0` (neutral),
then any remaining numeric LCID.  Some libraries (notably certain VB runtime sub-libraries)
are registered only under a language-specific LCID and have no neutral entry — skipping
non-zero LCIDs would silently miss them.

The `arch` subkeys (`win32`, `win64`) are iterated in preference order matching the current
process bitness.

### Embedded type libraries and resource-ID path suffixes

A type library need not exist as a standalone `.tlb` file.  It can be embedded as a Win32
resource inside a `.dll`, `.exe`, or `.ocx`.  When this is the case the registry value
contains a path with a trailing resource-ID number separated by a backslash:

```
C:\Windows\System32\MSVBVM60.DLL\2
```

`File.Exists` rejects such a path (the `\2` component is not a directory), but
`LoadTypeLib` from `oleaut32.dll` handles it natively. 

A single binary may contain multiple type library resources; each resource ID points to a
distinct `ITypeLib` with a different name and type set.

---

### Companion `.oca` files (VB6 OLE Control Archive)

When VB6 first loads an `.ocx` control it generates a companion `.oca` file in the same
directory.  This file is a cached, VB6-normalised copy of the type library.  It frequently
carries a **different library name** than the OCX itself — for example, the OCX resource
might export `ActiveBarLibrary` while the `.oca` exports `ActiveBarLibraryCtl`.  VB6 source
code that uses the control may reference either name.

`InspectAll` handles this by:

1. Checking for a sibling `{stem}.oca` next to the `.ocx` file.
2. Loading and inspecting the OCA as a separate type library.
3. Returning **both** results when their library names differ (deduplication by name).

The stub generator then generates stubs under both namespace names, ensuring that VB6 code
referring to either variant compiles correctly.

---

### Sibling file discovery

When `LoadTypeLib` fails for the primary path, or when the registry lookup for a GUID returns
a path that does not exist on disk, several fallback candidates are tried:

1. **All registered paths for the GUID** — by iterating the registry key for the GUID across
   all version/LCID/arch combinations, including the alternate hive (WOW6432Node).  The
   registry entry may use a resource-ID suffix pointing to a specific embedded type library
   resource.
2. **Sibling files with the same stem** — `.tlb`, `.olb`, `.ocx`, `.oca`, and `.dll` files
   in the same directory as the primary path, tried in that order.  This handles the common
   pattern where an OCX ships with a separate `.tlb` sidecar file.
3. **Registry-by-stem search** — the registry is scanned for any registered path whose
   filename stem (without extension) matches the primary path's stem.  This finds libraries
   that have moved or been re-registered under a different GUID.

Candidates are collected and deduplicated (by path, case-insensitively) before any
`LoadTypeLib` call is attempted.

---

## Type Metadata

Once a library file has been located, `oleaut32.dll`'s `LoadTypeLib` is called with the path
to obtain an `ITypeLib` pointer.  From there, type schema is retrieved through a two-level
hierarchy: 

- `ITypeLib.GetTypeInfoCount` returns the number of top-level types, and
- `ITypeLib.GetTypeInfo(i)` returns an `ITypeInfo` for each one.  
  
Calling `GetTypeAttr()` on an `ITypeInfo` yields a `TYPEATTR` struct that identifies 
the kind of type (`typekind`) and the counts of its members (`cFuncs`, `cVars`, `cImplTypes`).  

Members are then enumerated by calling `GetFuncDesc(j)` (for methods and property accessors) 
or `GetVarDesc(j)` (for fields and enum values) in index order.

Every descriptor must be explicitly released with `ReleaseFuncDesc`/`ReleaseVarDesc` when done 
— the COM type library API does not use reference counting for these sub-structures.

| COM concept | Role |
|---|---|---|
| `ITypeLib`  | One loaded type library |
| `ITypeInfo` | One type (class, interface, enum, struct, …) |
| `TYPEATTR`  | Attributes of a type (kind, member counts, …) |
| `FUNCDESC`  | One method or property accessor |
| `VARDESC`   | One field or enum value |
| `TYPEDESC`  | Recursive type descriptor (used for parameter and return types) |
| `ELEMDESC`  | Element descriptor wrapping a `TYPEDESC` plus parameter flags |

### Type kinds

The `TYPEATTR.typekind` field controls how a type is inspected:

| `TYPEKIND` | Description | C# stub |
|---|---|---|
| `TKIND_ENUM` | Enumeration (C# `enum`) |
| `TKIND_RECORD` / `TKIND_UNION` | Value type with fields (C# `record`) |
| `TKIND_MODULE` | Collection of global functions/constants (C# `static class`) |
| `TKIND_INTERFACE` | Pure vtable interface |
| `TKIND_DISPATCH` | Dispatch-only or dual interface |
| `TKIND_COCLASS` | Concrete class implementing one or more interfaces | 
| `TKIND_ALIAS` | `typedef` shorthand for another type (a `global using` alias) |

### Type names and namespaces

Every `ITypeLib` and every `ITypeInfo` can be queried for its documentation via
`GetDocumentation(-1, out name, …)`.  The returned name is exactly what goes into the C#
namespace (`ITypeLib` name) and class name (`ITypeInfo` name).

When a `TYPEDESC` with `VT_USERDEFINED` is encountered (a reference to another type), the code
calls `ITypeInfo.GetRefTypeInfo(hRefType)` to obtain the referenced `ITypeInfo`, then
`GetContainingTypeLib` to find its owning library.  The fully-qualified C# name is assembled as
`{SafeLibraryName}.{TypeName}`.

### DISPID semantics

The `DISPID` (dispatch ID) or `memid` (member ID) identifies a specific member (function, property), on a type,
and is returned as part of a `FUNCDESC` response for a given member. 

It encodes special roles for certain values:

| DISPID | Value | Meaning |
|---|---|---|
| `DISPID_VALUE` | 0 | Default member; invoked when the object is used directly with parentheses |
| `DISPID_UNKNOWN` | −1 | Sentinel returned by `GetIDsOfNames` when a name is not found; not a real member DISPID |
| `DISPID_PROPERTYPUT` | −3 | Implicit parameter name for the RHS value in an `INVOKE_PROPERTYPUT` dispatch call; not a member DISPID |
| `DISPID_NEWENUM` | −4 | Returns an `IEnumVARIANT` enumerator; marks the object as iterable |
| `DISPID_EVALUATE` | −5 | Evaluate / `[]` operator; very rarely appears in VB6-era type libraries |
| `DISPID_CONSTRUCTOR` | −6 | Constructor; essentially theoretical in the VB6 COM world |
| `DISPID_DESTRUCTOR` | −7 | Destructor; essentially theoretical in the VB6 COM world |
| `DISPID_COLLECT` | −8 | Parameterised default collection member; alternative to `DISPID_VALUE` for types that also have a parameterless default |
| All others | any | Ordinary dispatch ID; stored in the model but not used for dispatch logic |

**Default member (`DISPID_VALUE = 0`)**: Identified as `IsDefault = true` on the
`PropertyGet` or `PropertySet` with `memid == 0`.  In C#:

- If the default member has **parameters** → emitted as a C# indexer (`this[...]`).
  VB6 bang-operator patterns (`rs!Field`) and direct parenthesis invocations (`rs(key)`) both
  map to indexer access in the converted code.
- If the default member has **no parameters** → emitted as a regular named property.
  There is no C# equivalent of a parameterless default member.

**`DISPID_NEWENUM` (−4)**: When this member is encountered the inspector suppresses the raw
`_NewEnum` method and instead emits a `GetEnumerator` method returning
`System.Collections.IEnumerator`.  `System.Collections.IEnumerable` is also added to the
type's base interface list.  This enables `foreach` on the converted collection types.

**Two-hop default member (forwarding indexer)**: Some COM collection types (e.g. DAO.Recordset)
expose a DISPID 0 property with no parameters that returns a collection object, whose own
DISPID 0 member has parameters.  The stub generator detects this pattern and synthesises a
forwarding indexer on the outer type so that `rs["Field"]` compiles directly.

---

### Function-flag semantics: `FRESTRICTED` and `FHIDDEN`

Every `FUNCDESC` carries a `wFuncFlags` bitfield.  Two flags require special treatment:

#### `FUNCFLAG_FRESTRICTED` (0x1) — context-dependent

The meaning of this flag differs between interface kinds:

- **`TKIND_INTERFACE` (vtable interface)**: `FRESTRICTED` marks the inherited plumbing slots
  for `IUnknown` (`QueryInterface`, `AddRef`, `Release`) and `IDispatch`
  (`GetTypeInfoCount`, `GetTypeInfo`, `GetIDsOfNames`, `Invoke`).  These are always present
  in the `cFuncs` count but must be skipped; emitting them would produce uncompilable stubs
  that redeclare interface infrastructure.

- **`TKIND_DISPATCH` (dispatch interface)**: `FRESTRICTED` marks members that are not
  visible in the VB6 object browser but are nonetheless callable.  Examples include ambient
  property accessors like `ClientHeight` and `ClientWidth` that VB6 controls expose for
  container use.  These must **not** be skipped — VB6 code may invoke them, and dropping
  them silently would produce compile errors.

The inspector applies the restricted-skip logic only for `TKIND_INTERFACE`.

#### `FUNCFLAG_FHIDDEN` (0x40) — not currently filtered

Hidden members are excluded from the object browser but remain invocable.  The stub generator
does not filter on `FHIDDEN`; hidden members are included in stubs so that VB6 code that
calls them still compiles.

---

### VAR_DISPATCH properties in dispatch interfaces

Dispatch-only interfaces (written with ODL's `dispinterface { properties: ... methods: ... }`
syntax) sometimes describe their properties via `VARDESC` records with `varkind = VAR_DISPATCH`
rather than as `INVOKE_PROPERTYGET`/`INVOKE_PROPERTYPUT` function descriptors.  These are
read from `TYPEATTR.cVars` in a second pass after function inspection.

Each `VAR_DISPATCH` VARDESC:

- Yields a `PropertyGet` member.
- Always yields a `PropertySet` member, regardless of `VARFLAG_FREADONLY` (0x1).
- Carries its DISPID for disambiguation and its type resolved via `ResolveType`.

#### `VARFLAG_FREADONLY` is ignored for stub generation

`VARFLAG_FREADONLY` marks properties that cannot be assigned via `IDispatch` at runtime in VB6
code — for example, `TextBox.MultiLine` and `CommandButton.Style` are design-time-only in VB6
(attempting `txt.MultiLine = True` in code is a compile error).  However, VB6's form designer
sets these properties through `IPersistPropertyBag`, a persistence interface that is completely
separate from `IDispatch`.  The flag restricts `IDispatch::Invoke(DISPATCH_PROPERTYPUT)` only;
it has no effect on persistence-based initialisation.

The VB6Converter generates `InitializeComponent()` by converting every property entry in the
`.frm` file into a C# assignment statement.  The corresponding WinForms properties are always
writable at runtime, so emitting a read-only stub would cause those assignments to fail to
compile.  The inspector therefore ignores `VARFLAG_FREADONLY` and emits a setter unconditionally
for every `VAR_DISPATCH` property.

Without this second pass, any dispinterface that uses the `properties:` section syntax —
such as `stdole.Font` and many VB6 data-access interfaces — would appear to have no members.

---

### Coclass member harvesting

A `TKIND_COCLASS` declares a list of implemented interfaces via `TYPEATTR.cImplTypes`.
Members are not declared on the coclass itself; they are inherited from its interfaces.
The inspector:

1. Iterates `cImplTypes` using `GetImplTypeFlags` + `GetRefTypeOfImplType`.
2. Skips **source interfaces** (`IMPLTYPEFLAG_FSOURCE`): these are event-sink interfaces
   (the coclass *fires* events through them), not member providers.
3. For each remaining interface, calls `CollectInterfaceMembersRecursive` to walk the full
   inheritance chain (guarded by a `visitedInterfaces` GUID set to prevent cycles).
4. Deduplicates members by signature (`Kind:Name(paramTypes)=>ReturnType`) so that a member
   present on multiple implemented interfaces is only emitted once.

The list of non-source interface names is captured as `ImplementedInterfaces` so the
generated C# class can declare them as base types.

---

### Empty dispatch interfaces paired with vtable interfaces

Some COM libraries declare a companion pair:

- A **dispinterface** named `Foo` with zero function members.
- A **vtable interface** named `IFoo` carrying all the real members.

The relationship is not encoded in the type library; it relies entirely on the `I`-prefix
naming convention.  The inspector detects this pattern: after all types are collected, any
`DispatchInterface` with zero members for which a matching `Interface` named `I{Name}` exists
has that vtable interface appended to its `ImplementedInterfaces` list.  This ensures the
generated dispatch-interface stub inherits the vtable interface's member signatures and
compiles correctly when code references members through the dispatch interface name.

---

### Cross-library type references and namespace resolution (`VT_USERDEFINED`)

When a `TYPEDESC.vt` is `VT_USERDEFINED` the type is defined in another (or the same)
type library.  Resolution proceeds as follows:

1. **Dereference** — `ITypeInfo.GetRefTypeInfo(hRefType)` yields the referenced `ITypeInfo`.
2. **Follow alias chains** — if the referenced type is itself `TKIND_ALIAS`, recurse via
   `ResolveType` until a concrete type or primitive is reached.  Aliases resolve to C#
   primitive keywords; no namespace prefix is added.
3. **Identify the owning library** — `GetContainingTypeLib` returns the `ITypeLib` that owns
   the referenced type, and `GetLibAttr` yields its GUID and version, which is recorded as a
   `ComQueryDiscoveredDep`.  `GetDocumentation(-1)` on the owning library gives its name,
   which is normalised to a C# identifier (`MakeSafeName`) to form the namespace prefix.
4. **Check for a local redeclaration** — many VB6 ActiveX controls copy frequently-used enum
   types (e.g. `MousePointerConstants`) from the VB runtime directly into their own type
   library so that users do not have to add the runtime as a separate reference.
   `ITypeLib.FindName` on the *currently-inspected* library checks for this: if the type name
   also appears locally, the local library's namespace is preferred (because the stub
   generator will create a stub for the local copy, while the foreign library may not be in
   scope).
5. **Preserve canonical casing before `FindName`** — see the `ITypeLib::FindName` string
   mutation hazard documented above.  The canonical name from `GetDocumentation` is copied
   into a fresh `string` allocation before `FindName` is called.
6. **Return** `"{namespace}.{canonicalName}"` or just `"{canonicalName}"` when no namespace
   can be determined.

---

### Control detection and VB6-injected properties

#### Detecting ActiveX controls

A `TKIND_COCLASS` type is an ActiveX control when either:

- `TYPEATTR.wTypeFlags & TYPEFLAG_FCONTROL != 0` — the vendor explicitly marked it as a
  control in the type library, **or**
- The type library was loaded from a file with a `.ocx` or `.oca` extension.

The second condition is necessary because a significant number of older ActiveX controls omit
`TYPEFLAG_FCONTROL` from their coclass declarations.  Any coclass in a file delivered as an
`.ocx` is treated as a control unconditionally.

#### VB6 control extender properties

VB6's container runtime wraps every ActiveX control in an **extender object** that adds a
standard set of ambient properties on top of the control's own interface.  These properties
are injected at runtime by VB6; they do **not** appear anywhere in the COM type library.
VB6 code routinely reads and sets them directly on the control variable:

```vb6
MyControl.Left    = 100
MyControl.Top     = 50
MyControl.Visible = True
```

Without stubs for these properties the converted C# code will not compile.  The stub
generator detects `IsControl = true` and appends the following properties to every control
class stub (skipping any that the control's own TLB already defines):

| Property | C# type |
|---|---|
| `Left`, `Top`, `Width`, `Height` | `int` |
| `TabIndex` | `short` |
| `TabStop`, `Visible`, `Enabled` | `bool` |
| `Name`, `Tag`, `ToolTipText` | `string` |
| `HelpContextID`, `WhatsThisHelpID`, `DragMode` | `int` |
| `_ExtentX`, `_ExtentY`, `_StockProps`, `_Version` | `int` |

---

### Synthetic members override system

The stub generator supports a hand-authored `synthetic_members.json` file that can inject or
replace members on any COM type.  The format is a list of `SyntheticMemberSet` records, each
specifying:

- `Targets` — one or more `"LibraryName.TypeName"` strings (case-insensitive).
- `Members` — a list of `ComQueryMember` records to merge into the matched type.

Synthetic members go through the same `MergeMembers` deduplication and
`PickMoreSpecificMember` specificity logic as real members, so they can either supplement the
TLB data or override members that the type library reports with an overly-generic `object`
type.  Common uses:

- Adding members that the vendor omitted from the published TLB.
- Replacing `object`-typed parameters or return values with more specific types when the
  correct types are known from documentation or binary inspection.
- Adding `GetEnumerator`/`IEnumerable` support to collection types whose `_NewEnum` method
  is not present in the TLB.

---

## Known Issues and Nunances

### `GetRefTypeInfo` cross-library resolution fails from a 64-bit process

A subtle bitness problem affects `ITypeInfo::GetRefTypeInfo`, which is the call used to
resolve `VT_USERDEFINED` type references inside a loaded type library.  When a 32-bit OCX
refers to a type in another library (e.g. `stdole2.tlb`), the reference is encoded in the
TLB binary as an `hRefType` value.  Calling `GetRefTypeInfo` on that value causes
`oleaut32.dll` to look up the referenced library in the registry and load it.

When that call is made from a **64-bit process**, it consults the native (64-bit) registry
hive even though the type library being inspected was compiled as 32-bit and its cross-library
references point into the 32-bit (WOW6432Node) hive.  The result is `TYPE_E_CANTLOADLIBRARY`
(`0x80029C4A`) — the library cannot be found.

This failure is not remedied by manually pre-loading the referenced library (e.g. calling
`LoadTypeLib` on `stdole2.tlb` before the `GetRefTypeInfo` call).  `GetRefTypeInfo` does not
consult any process-level cache of already-loaded type libraries; it always goes back to the
registry.  The only reliable fix is to make the call from a **32-bit process**, where
`oleaut32.dll` transparently uses the WOW64-redirected hive.

This was observed concretely when inspecting `VsVIEW3.ocx` (VideoSoft vsPrinter3).  Its
`_DvsPrinter` dispatch interface declares `Font` and `HdrFont` as `VT_PTR → VT_USERDEFINED`
properties pointing to `IFontDisp` in `stdole2`.  From a 64-bit process `GetRefTypeInfo`
failed on both properties and the type resolver fell back to `object`.  From a 32-bit process
the same call succeeded and returned `StdType.Font` (the internal name of `stdole2.Font`).

### `ITypeLib::FindName` mutates .NET strings

`ITypeLib::FindName` (the COM IDL method) is declared with its first parameter as
`[in, out] LPOLESTR szNameBuf`.  The intent is that the caller passes a name to search for
and COM writes back the name with the canonical casing found in the type library (e.g. you
pass `"picture"` and COM writes back `"Picture"` if the type is spelled that way in the TLB).

In .NET's `System.Runtime.InteropServices.ComTypes.ITypeLib`, the signature is:

```csharp
void FindName(string szNameBuf, int lHashVal, ITypeInfo[] ppTInfo, int[] rgMemId, ref short pcFound);
```

`szNameBuf` is a plain `string`, not `ref string`.  One might expect that passing a C# string
by value means the interop layer creates a temporary native buffer and discards it after the
call — leaving the managed variable untouched.

**That expectation is wrong.**

The CLR's COM RCW passes the `LPOLESTR` of the managed string's internal character buffer
directly to COM.  When `FindName` writes its canonical casing back into that buffer, it
**mutates the content of the .NET string object in place**, bypassing the immutability
guarantee that C# programmers normally rely on.

In `TypeLibraryInspector.ResolveUserDefinedType`, the code first calls:

```csharp
refTypeInfo.GetDocumentation(-1, out string name, out _, out _, out _);
```

For `stdole.Picture`, this correctly returns `name = "Picture"` (capital P).

The code then calls:

```csharp
typeLib.FindName(name, 0, localInfos, localMemIds, ref found);
```

Here `typeLib` is the ActBar.ocx library.  ActBar has a property named `picture` (lowercase)
on its `IActiveBar` interface.  `FindName` matches this member (case-insensitively) and writes
back `"picture"` into the `name` variable's character buffer.  After the call, `name` silently
contains `"picture"` (lowercase p).  The return statement then produces `"stdole.picture"`
instead of the correct `"stdole.Picture"`.

To fix this, one needs to create a fresh independent string from `name`'s content 
**before** calling `FindName`. `new string(name.AsSpan())` allocates a new backing array, 
so COM's write-back into the old buffer does not affect the new string:

```csharp
// Preserve canonical casing before FindName mutates name's character buffer.
string canonicalName = new string(name.AsSpan());

typeLib.FindName(name, 0, localInfos, localMemIds, ref found);

// ... use canonicalName in the return, not name ...
return ns != null ? $"{ns}.{canonicalName}" : canonicalName;
```

In general, any `string` variable passed to a COM method where the corresponding IDL parameter is
`[in, out]` is at risk of silent in-place mutation.  The pattern to avoid the hazard is:
**copy the value you want to preserve into a new `string` allocation before making the call**.
`string canonicalName = new string(s.AsSpan())` is the idiomatic way to do this.

---

### struct layout cycles in stubs

Some COM type libraries (notably the Win32 OLE Automation library) define structs whose fields
create a cycle when interpreted as C# value types.  The canonical example is the
`TYPEDESC` / `ARRAYDESC` cluster:

```c
// Original C (simplified)
typedef struct tagTYPEDESC {
    union {
        struct tagTYPEDESC  *lptdesc;  // pointer — indirection present in C
        struct tagARRAYDESC *lpadesc;  // pointer — indirection present in C
        HREFTYPE             hreftype;
    };
    VARTYPE vt;
} TYPEDESC;
```

The COM type library (`.tlb`) records these union members with the pointer indirection
stripped: it exposes `lptdesc` as having type `TYPEDESC` (not `TYPEDESC*`).  When the stub
generator naïvely emits that as a `public TYPEDESC lptdesc;` field inside a C# struct, the
compiler raises **CS0523 "Struct member … causes a cycle in the struct layout"**.

#### Current behaviour

`ReferenceStubGenerator` runs a pre-pass over every library before emitting any files.  It
builds a dependency graph of same-library struct types (edges correspond to by-value field
types) and runs a DFS with tri-colour marking to find all back-edges.  Each back-edge
represents a field that would form a layout cycle.

For every identified cyclic field the generator emits:

```csharp
// was: Win.TYPEDESC — COM pointer field; replaced with nint to avoid struct layout cycle
public nint lptdesc;
```

`nint` (a pointer-sized integer) is the correct representation: the original C definition
used a pointer, and `nint` is the standard C# equivalent of a `void*`-width integer for
interop purposes.

#### Caveats

- **Field name is preserved** — call sites that read or write `lptdesc` still compile;
  only the type changes from the struct type to `nint`.
- **`nint` is not the original struct type** — code that passes the field to a method
  expecting `Win.TYPEDESC` will require an explicit `unsafe` dereference or cast.  In
  practice the stubs are not used at runtime (every method throws `NotImplementedException`),
  so this difference is invisible until the stubs are replaced with real implementations.
- **Cross-library cycles are not detected** — the DFS only follows edges within the same
  library.  Such cycles are believed not to exist in practice but would still cause a
  compiler error.

---

### optional `ref` parameters in stubs

Two related situations make it impossible to faithfully reproduce a COM method signature as a
C# stub:

1. **`[out]` + optional** — e.g. `ADODB.Command.Execute`'s `RecordsAffected` parameter is
   both ByRef and optional.  C# does not allow `ref` parameters to carry a default value.

2. **`[out]` required but following optional params** — e.g. `LoadPicture`'s `retval`
   parameter is required and ByRef, but it is declared after several optional inputs.  C#
   requires that once any parameter has a default, all subsequent parameters must too, so a
   bare `ref` param at the end is illegal.

#### Current behaviour

`BuildParameters` walks the list and tracks whether an optional parameter has been seen.
Any parameter that is either `IsOptional` itself, or that follows an optional parameter
(`forceOptional`), is emitted as a plain value type with `= default` — the `ref` modifier is
dropped in both cases.

The generated stubs compile and call sites that omit the argument work as expected.  Call
sites that *do* pass the argument no longer need `ref`, which is a semantic difference from
the original COM signature.  Because every stub method body throws `NotImplementedException`,
this difference is invisible at runtime until the stubs are replaced with real implementations.

#### If this becomes a problem

The correct fix is to emit two overloads: one with the optional-ref parameter present (as a
required `ref`) and one without it.  This mirrors the pattern used by Excel and other
COM-heavy libraries that expose Optional ByRef parameters.  Note that a method with *n*
optional-ref parameters requires up to 2ⁿ overloads, so the approach is best applied
selectively rather than universally.

---
