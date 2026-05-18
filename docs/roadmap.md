# VB6Converter Roadmap

This roadmap is based on the Optiware VB6-to-C# conversion sample and its generated output. The main takeaway is that the converter already handles surface-level syntax well, but it loses a lot of meaning at the semantic boundary: types, control flow, VB6 runtime behavior, and project-specific dependencies.

## Priority 1: Reduce unresolved symbols before compilation

The converted output still depends on many VB6-era and project-specific identifiers that are not bound in C#. The highest-value improvement is to make the converter build and emit a compatibility layer before Roslyn compilation runs.

Focus areas:
- VB6 runtime helpers such as `IsDate`, `DateStr`, `IsNumeric`, `CDate`, `FileSystem`, and `vb*` constants.
- DAO-style types such as `Recordset`, `RecordsetTypeEnum`, `QueryDef`, and related database helpers.
- Custom controls and OCX wrappers such as `IBDate`, `TDBGrid`, `DBCombo`, `MSMask.MaskEdBox`, and `vsPrinter`.
- Project-defined enums, constants, and modules that are used as global dependencies in VB6.

Recommended change:
- Build a project-wide symbol inventory from the parsed VB6 project.
- Generate or bind compatibility stubs for missing runtime and custom symbols before semantic rewriting.
- Track unresolved symbol categories separately so the converter can report whether failures are missing runtime shims, missing project references, or true conversion bugs.

## Priority 2: Improve type inference and cast insertion

The sample output leaves many values as `object`, which causes cascading errors such as invalid member access, bad indexing, and failed conversions. This is the main reason the output compiles poorly even when the syntax looks plausible.

Focus areas:
- Variables that behave like `Variant` in VB6 but have stable local usage patterns.
- DAO field reads and writes, where numeric/string/boolean values are inferred from context.
- Control values and form fields, especially `.Value`, `.Text`, `.Caption`, and `.Checked`-style members.
- Numeric conversions between `int`, `long`, `double`, `decimal`, and `bool`.

Recommended change:
- Add a stronger data-flow pass across assignments, comparisons, and call sites.
- Prefer explicit casts in generated C# over leaving ambiguous `object` values in place.
- Use domain hints from surrounding SQL, control metadata, and known APIs to choose stable types.

## Priority 3: Fix VB6 control-flow semantics

The diagnostics show a large amount of unreachable code, missing labels, and return-value confusion. That indicates the converter is preserving VB6 flow constructs too literally instead of mapping them into structured C#.

Focus areas:
- `On Error GoTo` blocks and label-based recovery paths.
- VB6 function return assignment patterns such as `MyFunction = value`.
- Early exits and label scopes inside procedures.
- `Exit Sub`, `Exit Function`, and `Resume` variants.

Recommended change:
- Normalize VB6 error-handling blocks into explicit `try/catch` or structured guard clauses earlier in the pipeline.
- Make return-value rewriting part of the main semantic model, not a late textual cleanup.
- Add regression tests for functions that return through assignment, multiple exit paths, or `Resume` labels.

## Priority 4: Handle VB6 preprocessing and conditional compilation

The conversion logs show `#If` blocks being treated as unknown statements. That means debug-only code and alternate compilation branches are not being modeled cleanly.

Focus areas:
- `#If`, `#ElseIf`, `#Else`, `#End If`.
- `#Const` and build-flag driven code paths.
- Nested conditional compilation around logging, debugging, and platform-specific code.

Recommended change:
- Evaluate or normalize preprocessor branches before statement conversion.
- Preserve only the active branch for the target configuration, but record skipped branches in diagnostics so users know what was excluded.
- Add parser coverage for common VB6 preprocessor idioms found in large applications.

## Priority 5: Add dedicated handling for forms and custom controls

The Optiware conversion shows many partially translated form members and legacy UI idioms. These are not just compile issues; they are behavior issues because VB6 control arrays and OCX members do not map directly to WinForms.

Focus areas:
- Control arrays and indexed default instances.
- VB6-specific properties such as `value`, `refresh`, `bookmark`, `list`, and `mouse pointer` behavior.
- Form-level globals and implicit instance access.
- OCX-backed controls that need wrapper types rather than generic `object` placeholders.

Recommended change:
- Generate typed wrapper classes for common control patterns.
- Treat form control metadata as a first-class input to the converter.
- Add a compatibility layer for frequently used control APIs instead of leaving them as direct property accesses.

## Priority 6: Support multi-dimensional array operations

The logs call out unsupported multi-dimensional `ReDim Preserve`. This is a recurring VB6 pattern and a good candidate for a focused feature because it frequently appears in data-heavy applications.

Recommended change:
- Add an explicit array model that can resize only the supported dimensions correctly.
- If a direct rewrite is not safe, generate a helper abstraction rather than silently dropping semantics.
- Emit a targeted diagnostic when the converter cannot preserve the exact behavior.

## Priority 7: Turn the sample project into a permanent regression corpus

The Optiware conversion is exactly the kind of real-world input the tool should learn from. It exposes behavior that synthetic tests will miss.

Recommended change:
- Keep per-file metrics for errors, warnings, unresolved symbols, and unsupported constructs.
- Add tests that assert diagnostic counts decrease for representative files over time.
- Preserve a small set of especially problematic inputs as regression fixtures for DAOs, forms, preprocessors, and control-flow-heavy modules.

## Suggested Execution Order

1. Add compatibility shims and symbol inventory support.
2. Strengthen type inference and explicit cast insertion.
3. Improve return-value and error-flow rewriting.
4. Normalize preprocessor directives.
5. Add control and OCX adapters.
6. Implement multi-dimensional array preservation.
7. Add regression metrics and project-level conversion baselines.

## Success Criteria

The roadmap is working if the following numbers trend down on the same real project:
- unresolved symbol diagnostics
- invalid member access diagnostics
- bad cast and indexing diagnostics
- unreachable-code warnings from control-flow rewrites
- unsupported VB6 construct errors

The target is not just fewer errors. The target is fewer structural conversions that still look like VB6 and more output that behaves like valid, idiomatic C#.