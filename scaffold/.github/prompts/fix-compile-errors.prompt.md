---
description: "Fix all Roslyn compile errors in a converted C# file, using the original VB6 source as the semantic reference"
argument-hint: "Relative path to the .cs file to fix, e.g. src/Customers.cs"
agent: "agent"
tools: [search, editFiles, runCommands]
---

Fix all compile errors in the C# file whose path is provided in the chat message.

## Steps

1. Read the target `.cs` file. Locate the `// Generated from: <vb6-path>` source-mapping comment at the top of the file to identify the original VB6 source.
2. Read the referenced VB6 source file.
3. Run `dotnet build 2>&1` from the `src/` directory and collect all diagnostics whose file path matches the target `.cs` file.
4. For each error or warning, use the VB6 source as the semantic ground truth to determine the correct fix. Prefer the simplest fix that preserves the original logic.
5. Apply all fixes to the `.cs` file in a single edit.
6. Re-run `dotnet build` scoped to the same file and confirm no errors remain.

## Rules

- Do not change method or property signatures unless directly required to fix a compiler error.
- Do not alter source-mapping comments (`// Generated from:` or per-method `// vb6/...:NN` comments).
- Do not add `using` directives for types already available via `_References/` COM stubs.
- If an error is caused by a missing member on a COM stub type (one under `_References/`), do not work around it with `dynamic` - instead add a `// TODO: missing COM stub member: <TypeName>.<MemberName>` comment on the call site and move on.
- Preserve the existing indentation and brace style.
- If the VB6 source and the C# disagree on logic (not just syntax), reproduce the VB6 behavior faithfully and add a `// NOTE: preserving VB6 behavior` comment.
