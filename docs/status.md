# ComWrapperGen Status

Date: 2026-08-06

## Overview

ComWrapperGen is a compile-first COM wrapper generator. It takes a COM library identifier, inspects the registered type library on Windows, and emits a new .NET 10 wrapper project that builds from generated source.

The generated output separates:
- Internal ABI-facing COM interfaces used for interop mechanics
- Public interfaces and wrappers used by consumers

This boundary is central to correctness and is now enforced in generation.

## End-to-End Flow

1. CLI parsing and orchestration
- Entry point: src/ComWrapperGen/Program.cs
- Inputs:
  - --lib or -l: COM library spec (name, GUID, or path)
  - --output or -o: output folder
  - --force: overwrite behavior
- Runtime guard: execution requires Windows

2. Library resolution
- Resolver: src/ComWrapperGen/ComLibraryResolver.cs
- Resolution modes:
  - Direct path to type library file
  - GUID with optional version suffix
  - Name match against HKCR\TypeLib entries
- Ambiguity handling:
  - If multiple name matches exist, generation stops with explicit candidate list

3. Type library inspection
- Inspector: src/ComWrapperGen/TypeLibraryInspector.cs
- Model definitions: src/ComWrapperGen/ComQueryModel.cs
- Inspector responsibilities:
  - Load typelib via OLE automation APIs
  - Enumerate types, members, parameters, enum values, and metadata
  - Track discovered dependencies
  - Handle OCX/OCA companion behavior for control libraries

4. Code generation
- Generator: src/ComWrapperGen/WrapperProjectGenerator.cs
- Outputs written under target directory:
  - <LibraryName>.csproj
  - internal/RuntimeSupport.cs
  - internal/*Internal.cs interfaces
  - Public interfaces
  - Coclass wrapper classes
  - Interface proxy classes
  - _GenerationReport.txt

5. Validation workflow
- Typical smoke path:
  - Build ComWrapperGen
  - Generate DAO output into scratch
  - Build generated DAO project
- Current known smoke target passes in this repository.

## Core Generated Layers

### Internal ABI Layer

Generated internal interfaces are intended for COM interop plumbing only.

Characteristics:
- GeneratedComInterface attributes
- PreserveSig on internal members
- Required marshalling attributes for selected bool/string cases
- COM plumbing members filtered from public surface

### Public API Layer

Generated public interfaces represent the user-facing contract.

Characteristics:
- Properties synthesized from matching property get/set member groups
- Methods emitted from non-plumbing COM members
- Enumerable facade exposed when typelib indicates NewEnum or enumerator shape

### Wrapper and Proxy Layer

Two forms are generated:
- Coclass wrappers: concrete activatable classes backed by COM CLSID
- Interface proxies: adapters wrapping internal interface instances

Both use forwarding with return and argument conversion rules.

## ABI to Public Boundary Rule

Correct conversion path is:
1. COM pointer result
2. Convert pointer to internal interface via ComRuntime.FromAbi<InternalInterface>
3. Wrap internal interface in generated PublicInterfaceProxy

Incorrect direct casting from ABI pointer to public interface is intentionally avoided.

This fix is applied in generated method and property return paths where ABI internal type is nint and managed return type is non-primitive interface-like.

## Typing Strategy

Current typing behavior is strong where resolution is reliable and conservative where it is not.

1. Public type map prefers interfaces over coclasses to reduce naming collisions.
2. Resolvable local interface references are emitted as strong public interface types.
3. Unknown or foreign type names fall back to object to preserve compileability.
4. Internal nint parameters and returns are bridged using conversion logic, including dynamic at selected call sites.

## Runtime Support in Generated Output

Generated internal runtime support includes:
- CoCreateInstance import and constants
- StrategyBasedComWrappers instance
- CreateInstance<TInterface>(Guid clsid)
- FromAbi<TInterface>(IntPtr ptr)

These are the core mechanics for activation and ABI pointer conversion.

## Packaging and Tooling

Project: src/ComWrapperGen/ComWrapperGen.csproj

Tool packaging is enabled with:
- PackAsTool true
- ToolCommandName comwrap
- PackageId comwrap

Important compatibility note:
- PackAsTool target framework remains net10.0 (not net10.0-windows).
- Runtime still enforces Windows for COM inspection and execution.

## Self-Containment

ComWrapperGen no longer depends on the external ComQuery project reference.

Inlined pieces include:
- ComQueryModel
- TypeLibraryInspector
- ReferenceNaming
- Log

This makes repository extraction straightforward and reduces cross-project coupling.

## Current Functional Status

Working:
- End-to-end generation pipeline
- Tool packaging and invocation path
- Internal/public/proxy boundary for ABI conversion
- DAO generation plus generated project build smoke validation

Known placeholders and limitations:
- AsEnumerable adapter currently throws NotSupportedException
- Dispatch and events are not yet fully modeled in runtime-equivalent behavior
- Some foreign typing paths intentionally remain object-based fallbacks

## Recommended Immediate Next Steps

1. Implement functional AsEnumerable adapter in generated proxies/wrappers.
2. Add structured diagnostics output for fallback and skipped signatures.
3. Split generator into explicit planning, model, and emission modules.
4. Expand dependency-aware cross-library typing and project composition.
5. Add focused tests around ABI-to-internal-to-public conversion boundaries.
