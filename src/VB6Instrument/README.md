# VB6Instrument

Build-time instrumentation for VB6 projects: adds **virtual line numbers**
(`Erl`-based diagnostics), **call traces**, and **error guards** (an
`Err`-chain that reads like a stack trace) to the sources, as a step in the
build process. Doing this by hand in canonical sources is cumbersome to
maintain; doing it mechanically on a staged copy keeps canonical sources
untouched and the instrumentation always reproducible.

- Works on the files referenced by a `.vbp`, a single file, or a directory
  tree (`.bas .cls .frm .ctl .dob .dsr .pag`; designer headers are skipped —
  only the code section after `Attribute VB_Name` is scanned).
- Operates **in place** on what it is given. **Staging a copy of the sources
  is the caller's responsibility** — never point it at canonical sources.
- Line-oriented: a dedicated VB6 statement tokenizer (strings, date literals,
  comments, `Rem`, colon-compounds, continuations), no ANTLR, no Roslyn —
  output is byte-faithful additive edit only.
- Encoding discipline: a UTF-8 BOM means UTF-8, anything else is CP1252
  (`--codepage`, default 1252). Line endings are preserved byte-exactly
  (CRLF expected; a missing final newline survives; lone CR is rejected).
  A BOM-less file is never re-encoded as UTF-8. Files are rewritten only
  when their bytes actually change — and then in a single write per file,
  after the file parsed completely and all insertions were collected.
  Any parse anomaly aborts the run with a nonzero exit and **no partial
  writes**.

## Installation

```pwsh
# from the repository root
dotnet publish src/VB6Instrument/VB6Instrument.csproj /p:PublishProfile=VB6Instrument
Copy-Item C:\Users\aj\source\publish\VB6Instrument\VB6Instrument.exe C:\Users\aj\Tools\
```

Framework-dependent single file (requires the .NET 10 runtime).

## Usage

```
VB6Instrument <project> [options]

<project>                 Path to the VB6 project (.vbp), a VB6 source file,
                          or a directory of VB6 sources. Instrumented in place.

--line-numbers            Pass A: virtual line numbers (10, 20, 30 ... per member)
                          and an instrumentation map (<project>.map).
--call-header <Statement> Pass B: trace statement prepended to every member;
                          {File} / {Member} placeholders.
--error-guard[=<Statement>] Pass C: error guards. With a value, the value is the
                          swallow-handler statement template ({file}/{member});
                          without a value the swallow handler emits Err.Clear only.
--error-propagation <FallThrough|Raise>
                          Re-raise handler strategy. Default: Raise (probe-verified).
--codepage <n>            Code page for BOM-less files. Default: 1252.
--whatif                  Report what would change without writing any file.
```

Examples:

```pwsh
# the full set, as used by the build
VB6Instrument.exe C:\Build\Staged\Optiware\Optiware.vbp `
    --line-numbers `
    --call-header "Debug.Write ""{File}.{Member}""" `
    --error-guard "ErrorHandle ""{file}"", ""{member}()"""
```

Typical build integration — the tool never sees canonical sources:

```pwsh
robocopy C:\Source\OptiwareVB6 C:\Build\Staged\OptiwareVB6 /MIR
VB6Instrument.exe C:\Build\Staged\OptiwareVB6\Optiware.vbp --line-numbers --call-header "Debug.Write ""{File}.{Member}""" --error-guard
if ($LASTEXITCODE -ne 0) { throw "instrumentation failed" }
# ... invoke VB6.EXE against the staged .vbp ...
```

## Pass A — virtual line numbers (`--line-numbers`)

Numbers executable statements inside member bodies (`Sub`, `Function`,
`Property Get|Let|Set`), starting at 10, step 10, per member — enabling
`Erl`-based diagnostics and `On Error GoTo <n>`.

- Never numbered: signatures, `End` lines, designer headers, bare
  `Dim/Const/Static/Attribute/Option/Implements/Declare/Event`, bare labels,
  comments, blank lines, `#If`-directives, continuation lines.
- Numbered: `ReDim`, colon-compounds, block keywords (`If`, `Else`, `Case`,
  `Next`, `Loop`, `Wend`, `End If`, ...). The **first `Case` after a
  `Select Case` is never numbered** (a compile error otherwise); the
  remaining `Case` clauses are.
- The body is pushed out to the numbering baseline (10 spaces) so the code
  column never moves; the number plus padding replaces the statement's own
  indent: `    If x Then` becomes `10        If x Then`. Non-statement lines
  (comments, continuations, directives) are pushed too; labels keep a
  legacy column-1 one-space form (`210 Abortar: On Error ...`); a statement
  whose indent is too narrow to hold its number stays pushed but unnumbered.
- Members that already carry numbers are left untouched (idempotence).
- A number above 65535 (`Erl` limit) is a hard failure.
- **Byte-compatibility**: this pass is a port of the incumbent
  `Set-Vb6LineNumbers.ps1` (Add mode) and must reproduce its output byte for
  byte. `test/VB6Instrument.Tests/Fixtures/LineNumbers/` holds the golden
  corpus; the port was additionally verified byte-identical over a 219-file
  real-world corpus.

### Instrumentation map (`<project>.map`)

With `--line-numbers`, the run also writes a JSON map describing the
instrumented sources: the project (from the .vbp, when the target is one),
and for every file the member inventory plus the translation from every
`Erl` number in the instrumented sources back to a line of the
**original, pre-instrumentation source** — the canonical source a developer
reads when an error report says `Form1.CloseSale (30)`. Pass A never
inserts or deletes lines, so it captures the original positions before the
line-inserting passes B/C run. The map sits next to the instrumentation
target, named after it: `VBProject.vbp` → `VBProject.vbp.map`, a single
source file → `Module1.bas.map`, a directory → `<dirname>.map`.

```json
{
  "project": {
    "name": "MyProject.vbp",
    "version": {
      "major": 1,
      "minor": 0,
      "revision": 0
    },
    "metadata": {
      "title": "My App",
      "fileDescription": "Does things"
    }
  },
  "files": {
    "forms/Form1.frm": {
      "name": "Form1",
      "type": "Form",
      "members": {
        "CloseSale": {
          "type": "Sub",
          "start": 18,
          "end": 40,
          "length": 23,
          "lines": {
            "10": 19,
            "20": 20,
            "30": 21
          }
        }
      }
    }
  }
}
```

Conventions:

- **Files** are keyed by path relative to the target root (the .vbp's
  directory, the given directory, or the single file's directory), with
  forward slashes.
- **`project.version`/`project.metadata`** come from the .vbp
  (`MajorVer/MinorVer/RevisionVersion` and the Make-tab strings `Title`,
  `VersionComments`, `VersionCompanyName`, `VersionFileDescription`,
  `VersionLegalCopyright`, `VersionLegalTrademarks`,
  `VersionProductName`); both are omitted when unknown (non-.vbp targets).
- **`file.name`** is the file's own `Attribute VB_Name` value (omitted when
  the file declares none); **`file.type`** is the .vbp-declared
  Module/Class/Form/Control when available, otherwise derived from the
  extension (`UserDocument` for .dob, `ActiveXDesigner` for .dsr,
  `PropertyPage` for .pag).
- **`members`** keys are the member names; Property Get/Let/Set share one
  name, so each accessor is suffixed (`Value.Get`, `Value.Let`,
  `Value.Set`). `start`/`end`/`length` are 1-based original-file lines
  (`length` = `end - start + 1`).
- **`lines`** are virtual → real (JSON requires string keys, so the number
  appears quoted). Numbers already present on skipped members (e.g.
  hand-numbered or incumbent-tool output) are mapped too — the map always
  describes the file's complete virtual-line state, making re-runs produce
  the identical map.
- The map is written only for clean runs (a failing run exits nonzero and
  leaves no partial artifacts) and never under `--whatif`.

## Pass B — call traces (`--call-header`)

Prepends one statement to every member as its first code-bearing line:

```vb
Public Sub CloseSale()
          Debug.Write "Form1.CloseSale"      ' {File}.{Member} rendered
    ...
```

- `{File}` is the source file name without extension, `{Member}` the member
  name; placeholder matching is case-insensitive.
- Inserted at the numbering baseline (10 spaces), **unnumbered** — it runs
  after pass A so `Erl` positions stay valid.
- A `' [NoTrace]` comment anywhere in the member skips it. Empty or
  comment-only bodies are skipped.
- Idempotence: the first code-bearing statement is matched against the
  template shape (placeholders wildcarded), so re-running never duplicates —
  and a file rename is tolerated.

## Pass C — error guards (`--error-guard`)

Inserts `On Error GoTo GuardError_<Member>` before the first code-bearing
body line and a handler immediately before the member's `End` line. Runs
**last** so the guard precedes the trace statement — an error inside the
trace call is contained too. Guard lines are unnumbered and do not shift
`Erl` for original statements. The exit statement inserted before the
handler label follows the member kind — `Exit Sub`, `Exit Function`, or
`Exit Property` (Get/Let/Set alike); a mismatched form would not compile.

**Re-raise mode** (default strategy `Raise`, probe-verified):

```vb
        Exit Sub
GuardError_CloseSale:
        Dim instrumentedGuardErl As Long: instrumentedGuardErl = Erl
        Err.Raise Err.Number, Err.Source, "Form1.CloseSale (" & instrumentedGuardErl & ") -> " & Err.Description
End Sub
```

The handler captures `Erl` at handler entry (the failing statement's
number), prepends its frame to `Err.Description`, and re-raises — errors
bubble outward, each previously-unguarded member contributing a frame, so
the description that reaches the host (or a swallow handler) reads like a
stack trace, outermost frame first:

```
Form1.Exit_Click -> Form1.CloseSale -> Object variable or With block variable not set
```

`Err.Number`/`Err.Source` pass through untouched; only the description is
decorated.

**Swallow mode**:

```vb
        Exit Sub
GuardError_Timer1_Timer:
        ErrorHandle "Form1", "Timer1_Timer()"   ' optional --error-guard value
        Err.Clear
End Sub
```

A clean normal exit with nothing pending to propagate.

**Mode classification**:

| Member | Mode |
|---|---|
| `Public` / `Friend` / `Global` members | re-raise |
| `Class_Initialize` | re-raise (a constructor failure must surface as a clean COM error) |
| `Private` members matching an event-handler pattern (`<id>_<event>`) | swallow |
| `Class_Terminate`; `UserControl_*` lifecycle members | swallow |
| `Private` members matching no event pattern | re-raise (adds a frame to the chain) |

Skip rules — a member is left byte-identical when:

- its body already contains an `On Error` statement in any form, detected in
  **masked code only** (string literals and comments don't count;
  colon-compounds do) — these manage their own errors;
- the body has no insertable first code statement (empty or comment-only);
- a `' [NoGuard]` comment is present;
- the generated label would collide with an existing label (suffix-uniquified;
  `[bracketed]` member names are sanitized for the label).

**Why `Raise` is the default** — the handler-semantics probe
(`HandlerSemanticsProbe/`) established that an active VB6 handler falling to
`End Sub` without `Resume` **swallows** the error instead of propagating it.
An explicit `Err.Raise` from inside the handler propagates reliably and
keeps the captured `Erl` in the description. `--error-propagation
FallThrough` emits the older decorate-and-fall shape; use it only knowingly.

## Failure handling and exit codes

| Exit | Meaning |
|---|---|
| 0 | success (changed or unchanged files) |
| 1 | one or more files failed (parse anomaly, encoding problem, number overflow) — failures are listed on stdout, successfully processed files are kept |
| 2 | usage problem (no pass requested, path not found, non-VB6 file) |

The build must treat a nonzero exit as fatal. Re-runs are always safe:
instrumenting an already-instrumented tree changes nothing (verified by
tests and on a 219-file real-world corpus with all passes).

## Development

- Solution: `VB6Converter.slnx` — `src/VB6Instrument` +
  `test/VB6Instrument.Tests` (MSTest + AwesomeAssertions).
- `test/VB6Instrument.Tests/Fixtures/LineNumbers/` — the pass A golden
  corpus (`Input/` → `Expected/`, byte-exact, including CP1252, UTF-8 BOM
  and missing-final-newline cases).
- `src/VB6Instrument/HandlerSemanticsProbe/` — scratch VB6 ActiveX DLL that
  established the error-handler facts (§6 of the design); its README records
  the results and how to re-run it.
- Differential validation against the incumbent PowerShell tooling:
  run `Set-Vb6LineNumbers.ps1` and the tool over the same tree and compare
  bytes — zero differences is the acceptance criterion for pass A.
