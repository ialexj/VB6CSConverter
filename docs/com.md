# COM Type Libraries and Reference Stub Generation

## What is COM, from the stub generator's perspective

COM (Component Object Model) is the binary interface standard that VB6 uses for every external
library — from the VB6 runtime itself (`MSVBVM60.DLL`) to third-party ActiveX controls
(`.ocx`).  Each COM library ships with a **type library** (`.tlb`, or embedded as a resource
inside a `.dll` or `.ocx`) that describes every public type, method, property, enum, and struct
the library exposes.

The stub generator reads those type libraries using the Windows OLE Automation API
(`oleaut32.dll`) to produce C# stub classes that satisfy the Roslyn compiler during the
conversion process.  The stubs are *thin*: they have the right names, namespaces, and signatures,
but every method body just throws `NotImplementedException`.  Their sole purpose is to let the
converted C# code compile before the real runtime libraries are wired up.

### Key data structures

| COM concept | .NET interop type | Role |
|---|---|---|
| `ITypeLib` | `System.Runtime.InteropServices.ComTypes.ITypeLib` | One loaded type library |
| `ITypeInfo` | `System.Runtime.InteropServices.ComTypes.ITypeInfo` | One type (class, interface, enum, struct, …) |
| `TYPEATTR` | `System.Runtime.InteropServices.ComTypes.TYPEATTR` | Attributes of a type (kind, member counts, …) |
| `FUNCDESC` | `System.Runtime.InteropServices.ComTypes.FUNCDESC` | One method or property accessor |
| `VARDESC` | `System.Runtime.InteropServices.ComTypes.VARDESC` | One field or enum value |
| `TYPEDESC` | `System.Runtime.InteropServices.ComTypes.TYPEDESC` | Recursive type descriptor (used for parameter and return types) |
| `ELEMDESC` | `System.Runtime.InteropServices.ComTypes.ELEMDESC` | Element descriptor wrapping a `TYPEDESC` plus parameter flags |

### Type kinds

The `TYPEATTR.typekind` field controls how a type is inspected and what kind of C# stub is
emitted:

| `TYPEKIND` | Description | C# stub |
|---|---|---|
| `TKIND_ENUM` | Enumeration | `enum` |
| `TKIND_RECORD` / `TKIND_UNION` | Value type with fields | `struct` |
| `TKIND_MODULE` | Collection of global functions/constants | `static class` |
| `TKIND_INTERFACE` | Pure vtable interface | `class` (methods `throw`) |
| `TKIND_DISPATCH` | Dispatch-only or dual interface | `class` (methods `throw`) |
| `TKIND_COCLASS` | Concrete class implementing one or more interfaces | `class` — members are harvested from the default non-source interface |
| `TKIND_ALIAS` | `typedef` shorthand for another type | `global using` alias |

### Type names and namespaces

Every `ITypeLib` and every `ITypeInfo` can be queried for its documentation via
`GetDocumentation(-1, out name, …)`.  The returned name is exactly what goes into the C#
namespace (`ITypeLib` name) and class name (`ITypeInfo` name).

When a `TYPEDESC` with `VT_USERDEFINED` is encountered (a reference to another type), the code
calls `ITypeInfo.GetRefTypeInfo(hRefType)` to obtain the referenced `ITypeInfo`, then
`GetContainingTypeLib` to find its owning library.  The fully-qualified C# name is assembled as
`{SafeLibraryName}.{TypeName}`.

---

## Known COM interop hazard: `ITypeLib::FindName` mutates .NET strings

### Background

`ITypeLib::FindName` (the COM IDL method) is declared with its first parameter as
`[in, out] LPOLESTR szNameBuf`.  The intent is that the caller passes a name to search for
and COM writes back the name with the canonical casing found in the type library (e.g. you
pass `"picture"` and COM writes back `"Picture"` if the type is spelled that way in the TLB).

### The problem

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

### Observed symptoms

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

### The fix

Create a fresh independent string from `name`'s content **before** calling `FindName`.
`new string(name.AsSpan())` allocates a new backing array, so COM's write-back into the old
buffer does not affect the new string:

```csharp
// Preserve canonical casing before FindName mutates name's character buffer.
string canonicalName = new string(name.AsSpan());

typeLib.FindName(name, 0, localInfos, localMemIds, ref found);

// ... use canonicalName in the return, not name ...
return ns != null ? $"{ns}.{canonicalName}" : canonicalName;
```

### Generalisation

Any `string` variable passed to a COM method where the corresponding IDL parameter is
`[in, out]` is at risk of silent in-place mutation.  The pattern to avoid the hazard is:
**copy the value you want to preserve into a new `string` allocation before making the call**.
`string canonicalName = new string(s.AsSpan())` is the idiomatic way to do this.

---

## Known limitation: struct layout cycles in stubs

### Background

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

### Current behaviour

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

### Caveats

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

## Known limitation: optional `ref` parameters in stubs

### Background

Two related situations make it impossible to faithfully reproduce a COM method signature as a
C# stub:

1. **`[out]` + optional** — e.g. `ADODB.Command.Execute`'s `RecordsAffected` parameter is
   both ByRef and optional.  C# does not allow `ref` parameters to carry a default value.

2. **`[out]` required but following optional params** — e.g. `LoadPicture`'s `retval`
   parameter is required and ByRef, but it is declared after several optional inputs.  C#
   requires that once any parameter has a default, all subsequent parameters must too, so a
   bare `ref` param at the end is illegal.

### Current behaviour

`BuildParameters` walks the list and tracks whether an optional parameter has been seen.
Any parameter that is either `IsOptional` itself, or that follows an optional parameter
(`forceOptional`), is emitted as a plain value type with `= default` — the `ref` modifier is
dropped in both cases.

The generated stubs compile and call sites that omit the argument work as expected.  Call
sites that *do* pass the argument no longer need `ref`, which is a semantic difference from
the original COM signature.  Because every stub method body throws `NotImplementedException`,
this difference is invisible at runtime until the stubs are replaced with real implementations.

### If this becomes a problem

The correct fix is to emit two overloads: one with the optional-ref parameter present (as a
required `ref`) and one without it.  This mirrors the pattern used by Excel and other
COM-heavy libraries that expose Optional ByRef parameters.  Note that a method with *n*
optional-ref parameters requires up to 2ⁿ overloads, so the approach is best applied
selectively rather than universally.

---

## Reference stub generation pipeline

The following steps are executed by `Program.GenerateReferenceStubsAsync`, driven by the
loaded `VisualBasicProject`.

### Step 1 — Load the project and collect references (`VisualBasicProject.Load`)

The `.vbp` file is parsed line by line.  Two kinds of library lines are recognised:

- `Reference=*\G{GUID}#major.minor#lcid#path#description` — type library references
- `Object={GUID}#major.minor#lcid; filename.ocx` — ActiveX control references

Two implicit references are always appended whether or not the project declares them:

| Library | GUID | Role |
|---|---|---|
| VBA / Visual Basic For Applications | `{000204EF-...}` | Core VB runtime types (String, Integer, …) |
| stdole2 / OLE Automation | `{00020430-...}` | `Picture`, `Font`, `IDispatch` |

Each reference becomes a `VisualBasicProjectReference` record holding the GUID, version,
declared path, and a *resolved* path.

### Step 2 — Resolve library paths (`VisualBasicProject.ResolveReferencePath`)

For each reference the physical file is located:

1. **Declared path** — the path embedded in the `.vbp` line is tried first (absolute or
   relative to the project directory).
2. **Registry fallback** — when the file doesn't exist, the registry key
   `HKCR\TypeLib\{GUID}\major.minor\lcid\win32` (or `win64`) is consulted.

Registry lookup subtleties:

- LCID subkeys are iterated in preference order: the reference's own LCID, then `0` (neutral),
  then any other numeric LCID.  Some libraries (e.g. the VB runtime sub-library
  `{EA544A21-...}`) are only registered under LCID 9 (English) and not under 0.
- The registry value may contain a resource-ID suffix (`MSVBVM60.DLL\3`).
  `File.Exists` rejects such paths; `IsTypeLibPath` strips the trailing `\N` and checks the
  base file instead.

### Step 3 — Inspect each type library (`TypeLibraryInspector.Inspect`)

For every resolved reference, `LoadTypeLib` (from `oleaut32.dll`) loads the type library into
memory.  The library is then walked type-by-type:

```
ITypeLib.GetTypeInfoCount()  →  loop 0..N
  ITypeLib.GetTypeInfo(i)    →  ITypeInfo
    GetDocumentation(-1)     →  type name
    GetTypeAttr()            →  TYPEATTR  (typekind, cFuncs, cVars, cImplTypes)
```

Depending on `typekind`:

- **Enum** — `GetVarDesc` loops yield `LibraryEnumValueModel(name, value)`.
- **Struct/Union** — `GetVarDesc` loops yield field names and types.
- **Coclass** — `GetImplTypeFlags` and `GetRefTypeOfImplType` find the default non-source
  interface; members are then harvested from that interface.
- **Interface / DispatchInterface / Module** — `GetFuncDesc` loops yield methods and
  property accessors.

For every function or property:

```
FUNCDESC.invkind   → PropertyGet / PropertySet / Method
FUNCDESC.memid     → used with GetDocumentation to get the member name
FUNCDESC.elemdescFunc.tdesc  → return type  (via ResolveType)
FUNCDESC.lprgelemdescParam   → parameter types and names
```

`ResolveType` maps `TYPEDESC.vt` (a `VarEnum`) to C# type strings.  `VT_PTR` is unwrapped
recursively; `VT_USERDEFINED` triggers `ResolveUserDefinedType`, which:

1. Calls `ITypeInfo.GetRefTypeInfo(hRefType)` to get the referenced `ITypeInfo`.
2. Follows `TKIND_ALIAS` chains recursively to a primitive.
3. Calls `GetContainingTypeLib` to learn the owning library's namespace.
4. Calls `ITypeLib.FindName` on the *currently-inspected* library to check for a local
   redeclaration (some VB6 controls copy enum types from VBRUN into their own TLB; when
   found locally a stub will be generated for them and the local namespace is preferred).
5. Returns `"{namespace}.{canonicalName}"` where `canonicalName` was captured before the
   `FindName` call (see the FindName mutation hazard above).

Each library inspection also accumulates a `HashSet<DiscoveredDependency>` — the GUIDs of
every foreign library encountered while resolving `VT_USERDEFINED` types.

### Step 4 — Generate C# stub files (`ReferenceStubGenerator.Generate`)

For each `LibraryModel`, stub files are written under `_Reference/{SafeLibraryName}/`:

| Source kind | Output |
|---|---|
| Enum | `{Name}.cs` — C# `enum` with all values |
| Struct | `{Name}.cs` — C# `struct` with all fields as auto-properties |
| Interface / DispatchInterface / Coclass / Module | `{Name}.cs` — C# `class` with stubs for every method and `{ get; set; }` for every property pair |
| Alias | `_Aliases.cs` — `global using {Name} = {CSharpType};` |

All emitted classes carry `[System.CodeDom.Compiler.GeneratedCode]` so the converter knows it
is safe to overwrite them on re-run.

### Step 5 — Transitive dependency scan

After the explicit references are processed, `models.DiscoveredDependencies` is walked.
Any library whose GUID was not already in the project's explicit reference list is resolved
via the registry (Step 2) and inspected (Step 3) and stubbed (Step 4).  Newly discovered
dependencies from those secondary libraries are queued for the same treatment, continuing
until the queue is empty.

This ensures that if (for example) an ActiveX control exposes a type from `stdole2` and
`stdole2` is not listed in the `.vbp` file, its stubs are still generated.

### Step 6 — Generate `_ReferenceUsings.cs` (`ReferenceUsingsGenerator.Generate`)

A single `_ReferenceUsings.cs` file is written to the output directory containing:

```csharp
global using ActiveBarLibrary;
global using stdole;
// ...

global using static ActiveBarLibrary.SomeEnum;
// ...
```

This makes all reference types and all enum members available throughout the converted C#
project without explicit using directives in every file, mirroring VB6's project-wide
visibility model.
