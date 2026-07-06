# Agent Guide — Completing a VB6 → C# Conversion

This project was produced by **VB6Converter**, an automated tool that parses a VB6 codebase
(ANTLR grammar) and emits an equivalent C# project via Roslyn syntax generation and a chain of
rewriters. The output is **not hand-written C#** — it is best-effort machine translation that
gets most of the syntax right but leaves semantic gaps. Your job is the **last mile**: turn this
into a project that compiles cleanly and behaves like the original VB6 application, not just
"looks like" it.

Treat the original VB6 source (if present in this repo, or in a sibling `vb6/` folder) as the
**ground truth for behavior**. When C# and VB6 disagree on logic (not just syntax), reproduce the
VB6 behavior — do not "improve" it unless asked to.

The conversion deliberately targets **COM stubs and `Microsoft.VisualBasic` runtime compatibility**
rather than idiomatic .NET types. Forms, controls, and VB6 intrinsic objects keep their VB6-shaped
API (backed by a stub type under `_References/`) instead of being rewritten to `System.Windows.Forms`
equivalents. The only exceptions are a handful of places where the .NET equivalent is a trivial,
unambiguous 1:1 substitute (e.g. `MsgBox` → `MessageBox.Show`, VB6 `Cursor`/`Keys` constants →
`System.Windows.Forms` enums). Do not take this as license to rewrite other control/form usage to
WinForms on your own initiative — that is an explicit non-goal of this conversion.

---

## How this code was generated (pipeline summary)

```
VB6 project (.vbp)
  → COM reference inspection (x86 + x64) → _References/{Lib}/{Type}.cs stub classes
  → Preprocess each source file (strip line numbers, join continuations, split labels)
  → ANTLR parse → Roslyn AST (ClassConverter/StatementConverter/DeclarationConverter/ValueConverter)
  → Initial rewriters (no type info): literals, VB6 runtime calls, keyword escaping, MsgBox/Cursor/Keys, usings
  → Written to disk as .cs files
  → Reload as a Roslyn project, then an iterative semantic-rewriter loop runs until stable:
      control singletons, type inference, member resolution, array-vs-call disambiguation,
      parameterized-property rewriting, cross-file type refinement, cast insertion, literal coercion
  → _Diagnostics.txt written summarizing remaining Roslyn errors/warnings
```

There is no separate "missing type generation" phase — any VB6 type (including intrinsic objects
like `Form`, `TextBox`, `CommandButton`) is resolved the same way as any other COM reference: by
inspecting the relevant type library and emitting a stub under `_References/`. If a symbol is still
unresolved after that, it's a genuine gap (missing/incorrect COM reference, unsupported construct,
or a project-specific global) rather than something the converter auto-stubs after the fact.

Nothing in this pipeline runs a full semantic understanding pass equivalent to a human — it is
pattern-based. Expect the tool to have gotten the "shape" of the code right and the fine-grained
typing/control-flow wrong in places.

---

## Artifacts and markers to look for

These are the breadcrumbs the converter leaves behind. Search for them before assuming something
is broken by accident — they usually point at exactly what still needs attention.

| Marker | Meaning |
|---|---|
| `// Generated from: <vb6-relative-path>` (top of file) | Maps this C# file back to its VB6 source. Note: a sidecar copy of the raw VB6 source is **not** produced by the converter — use this path to go find the original file instead. |
| `// Generated from: <vb6-relative-path>:<line>` (above a member) | Maps a single method/property back to the exact VB6 line it came from. **Use this to find ground truth quickly.** |
| `// ERROR: <message> @ <line>:<col>` | The converter could not translate this construct; the surrounding code is a best-effort placeholder (often `default` or an empty statement). These need real implementations. |
| `.log` file next to a `.cs` file | Parse/transform error detail for that source file. |
| `_Diagnostics.txt` (output root) | Full Roslyn diagnostics report grouped by file and severity. **Start here.** |
| `[GeneratedCode("VB6Converter", "<timestamp>")]` | Marks a fully machine-generated file/type. |
| `_VB6Usings.cs` | Global `using static` directives for VB6 runtime compatibility shims (`Microsoft.VisualBasic.Strings`, `FileSystem`, `VBMath`, `Conversion`, `DateAndTime`, `Interaction`, `Constants`, `ControlChars`). These provide `Trim`, `Dir`, `Rnd`, `IsDate`, etc. without qualification. |
| `_References/{LibrarySafeName}/{TypeName}.cs` | COM type-library stub classes/interfaces, generated for **every** COM reference, including the intrinsic VB6 runtime library (`VB.Form`, `VB.TextBox`, `VB.CommandButton`, etc.). Members exist with correct signatures but **bodies `throw new NotImplementedException()`**. These are compile-time placeholders, not working implementations. |
| `_ReferenceUsings.cs` | Global `using`/`using static`/alias directives for COM reference namespaces and enums. |
| `_ComStubInterfaces.cs` | Marker interfaces (`IComStub`, `IOleStub`, `IControlStub<T>`) applied to COM/control stub types; `IControlStub<T>` simulates the VB6 extender's `.Object` property. |

---

## What the converter already did (don't redo it, but don't trust it blindly)

- **Modules (`.bas`) → static classes**, **classes (`.cls`) → classes**, **forms (`.frm`)/controls (`.ctl`) → classes whose base type and control fields are the corresponding COM stub types under `_References/VB/`** (e.g. a form's base class is the stub `VB.Form`, not `System.Windows.Forms.Form`). This is intentional: the conversion targets the COM-stub/`Microsoft.VisualBasic` compatibility layer rather than rewriting the UI to native WinForms.
- VB6 literals (dates, hex/octal numbers, string escaping) → C# literal syntax.
- VB6 runtime calls (`Trim`, `Format`, `IsNumeric`, etc.) rewritten to `Microsoft.VisualBasic.*` calls, available unqualified via `_VB6Usings.cs`.
- Identifiers colliding with C# keywords are escaped with `@` (e.g. `@class`).
- `MsgBox` → `MessageBox.Show`; VB6 `Cursor`/`Keys` constants → `System.Windows.Forms` enums.
- `FunctionName = value` return-assignment pattern → `return value;`.
- `On Error GoTo` → approximate `try`/`catch` — **this is one of the least reliable rewrites**; treat converted error handling with suspicion and compare against the VB6 `On Error`/`Resume` logic.
- Form controls become singleton instance fields; `ForEach` loop variables get type fixups.
- Several passes try to replace `object`-typed variables/properties with a concrete type inferred
  from usage (assignments, member access, disambiguating `arr(i)` array-index vs. `fn(i)` call,
  parameterized-property setters). Many variables will still be left as `object` where inference
  wasn't confident — this is expected, not necessarily a bug, but is the largest single source of
  remaining compile errors (bad casts, invalid member access, ambiguous overloads).
- DAO-specific rewriting exists in the codebase but is **currently disabled** — DAO `Recordset`/`Field`/`QueryDef` code is likely to need manual attention.

---

## Known systematic gaps (expect these, prioritize accordingly)

1. **Unresolved symbols** — VB6 runtime helpers, DAO types, third-party OCX controls
   (grids, masked edit, date pickers, etc.), and project-level globals may not have real bindings.
   Check `_References/` first; a "missing member"/"missing type" error is often a COM stub that
   was never generated (missing or unresolved reference) or is missing a member, rather than
   something the converter will fill in automatically later in the pipeline.
2. **`object`-typed values** — the single biggest source of cascading errors (bad casts, invalid
   member access, bad indexing). Prefer inserting an explicit, correct type/cast over silencing
   the error with `dynamic` or another cast to `object`.
3. **Control-flow drift** — unreachable code, orphaned labels, `Resume`/`Resume Next` semantics not
   fully captured, multiple exit paths. Compare against the VB6 `On Error`/`GoTo`/`Exit` structure.
4. **Conditional compilation (`#If`/`#ElseIf`/`#Const`)** — generally not evaluated; treated as an
   unknown/unsupported construct. Check whether debug-only or platform-specific branches were
   silently dropped or left as dead code.
5. **Multi-dimensional `ReDim Preserve`** — not reliably supported; look for TODO/error markers
   near array resizing code.
6. **FRX binary resources** (icons, bitmaps, list contents embedded via `"Form1.frx":OFFSET`) —
   unresolved references are emitted as `default` with an `// ERROR: Unresolved FRX resource: ...`
   comment. These need the actual resource extracted and wired up (e.g. `.Items.Add(...)` for
   list contents, an embedded resource for images).
7. **COM stub member collisions** — a stub type can occasionally emit both an indexer (`this[...]`)
   and a same-named `Item` property, which is a C# compile error (CS0102/CS0111). If found in
   `_References/`, fix by keeping only the indexer (that's the one VB6 default-member calls need).
8. **`Microsoft.VisualBasic` enum name mismatches** — most VB6 intrinsic constant groups
   (`TriState`, `DateFormat`, `FileAttribute`, `VbStrConv`, `FirstDayOfWeek`, `AppWinStyle`) map
   1:1 to their `.NET` enum names, but `VariantType` does **not**: VB6 `vbInteger` (2) maps to
   .NET `VariantType.Short`, and VB6 `vbLong` (3) maps to `VariantType.Integer` (reflecting VB6's
   16-bit `Integer`/32-bit `Long` vs. .NET's `Int16`/`Int32`). Don't rename by naive string match.

---

## Workflow for closing the loop

1. **Build first.** Run `dotnet build` (or the equivalent task) from the directory containing the
   generated `.csproj`. Do not start guessing at fixes before seeing real diagnostics.
2. **Read `_Diagnostics.txt`** if present — it's pre-grouped by file and diagnostic ID/count.
   Fix the highest-count diagnostic IDs and the files with the most errors first; fixing one root
   cause (e.g. a bad COM stub signature, a wrong global using) often clears many errors at once.
3. **For each error, find the VB6 ground truth** via the `// Generated from:` comment (file-level
   and per-member) before changing anything. Prefer the simplest fix that preserves the original
   VB6 behavior.
4. **For missing/wrong members on a `_References/` COM stub type**: if the fix is simple (wrong
   parameter type, wrong return type), fix the stub directly. If the real behavior is unknown,
   don't paper over it with `dynamic` — leave a `// TODO: missing COM stub member: <Type>.<Member>`
   comment and move on; these need a human or COM documentation to resolve correctly.
5. **Don't delete `// ERROR:` or `// Generated from:` comments** — they're the audit trail back to
   the VB6 source and to what the converter couldn't handle. Remove an `// ERROR:` comment only
   once you've actually implemented the real behavior it was marking.
6. **Recompile iteratively**, file-by-file or in small batches, rather than attempting a single
   pass across the whole project.
7. **If the same category of bug appears in many files** (e.g. every DAO recordset loop has the
   same wrong pattern), consider whether it should be fixed in the `VB6Converter` tool itself
   (if you have access to that repository) rather than hand-patched dozens of times here.

## Rules

- Preserve VB6 semantics faithfully. If VB6 and the generated C# disagree on logic, reproduce the
  VB6 behavior and note it with `// NOTE: preserving VB6 behavior`.
- Don't change method/property signatures unless required to fix a real compiler error.
- Don't add workaround `using`/`dynamic` for a type that already exists under `_References/` —
  fix or flag the stub instead.
- Don't bulk-rewrite files carrying the `[GeneratedCode]` attribute without checking whether a
  person has already started cleaning them up (look for non-generated comments, removed markers).
- If `.github/prompts/fix-compile-errors.prompt.md` or `.github/prompts/compare-method.prompt.md`
  are present in this repo, prefer using them for their respective tasks (fixing a single file's
  compile errors; diffing a converted method against its VB6 origin) instead of reinventing the
  same process ad hoc.
