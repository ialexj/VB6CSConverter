---
description: "Compare a converted C# method against its VB6 origin and report semantic drift or suggest corrections"
argument-hint: "Method name and .cs file, e.g. Customers.Save in src/Customers.cs"
agent: "agent"
tools: [search, editFiles]
---

Compare the C# implementation of the method specified in the chat message against its VB6 original to detect semantic drift.

## Steps

1. Locate the method in the specified `.cs` file.
2. Read the per-method source-mapping comment (e.g. `// vb6/Customers.cls:47`) to find the exact line in the VB6 source.
3. Read the VB6 source file. Extract the method body from that line to its matching `End Sub` / `End Function` / `End Property`.
4. Extract the C# method body.
5. Compare the two implementations side-by-side and identify any of these classes of drift:
   - Logic drift: conditions, loop bounds, or branch order differ
   - Missing statements: a VB6 statement has no equivalent in the C# output
   - Extra statements: C# contains statements with no VB6 origin
   - Type coercion drift: VB6 implicit coercions that were not reproduced
   - Error-handling drift: `On Error`/`Resume` semantics not captured by the generated try/catch

## Output format

### Method
`<ClassName>.<MethodName>` - VB6 origin: `<vb6-path>:<line>`

### Drift findings
For each finding: Category | VB6 lines | C# lines | Description

### Verdict
- Clean - no semantic differences found
- Minor drift - cosmetic or equivalent differences, no action required
- Fixable drift - concrete fixes proposed below
- Review required - differences are ambiguous; human review recommended

### Proposed fixes (if Fixable drift)
Show the corrected C# snippet only for the drifted sections.

## Rules

- Do not edit any file unless the verdict is "Fixable drift" and you are confident in the correction.
- Treat VB6 as ground truth.
- Do not attempt to fix errors visible in the C# that are outside the scope of the requested method - use fix-compile-errors for that.
