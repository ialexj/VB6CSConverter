# Handler-semantics probe (Step 0)

Establishes the four empirical facts the error-guard pass depends on
(spec §6). **Run on 2026-11-25 — results recorded below; the `raise`
strategy is now the tool's default.** Re-run after any VB6 runtime
update that could change handler semantics.

## Recorded results (2026-11-25)

| # | Fact | Result | Consequence |
|---|------|--------|-------------|
| 1 | `Err.Description` assignable inside an active handler | **True** | decoration works |
| 2 | Fall-to-`End` propagation | **Does NOT propagate** — an active handler that falls to `End Sub` without `Resume` swallows the error and returns normally | the `raise` strategy (explicit `Err.Raise` with the captured Erl baked in) is the default; `fallthrough` remains available behind `--error-propagation` |
| 2b | Undecorated propagation preserves `Number`/`Source`/`Description` | True (`Number=5 Source=probe.source Description=inner description`) | `Err.Raise Err.Number, Err.Source, …` preserves identity |
| 2c | `Erl` inside the raise handler at decoration time | **10** (the erroring line) — `InnerDecorated (10) -> inner description` | positions in the decoration are correct |
| 2d | Caller-side `Erl` of an undecorated propagated error | 0 (but with the raise strategy the outer handler captures its own numbered call line: `ThrowDecorated (10)`) | outer chain frames show their true call-site position |
| 3 | `Err.Clear` + fall-to-`End` exits a swallow handler cleanly | **True** | swallow mode unchanged |
| 4 | Decorated description arrives as the host exception message | **Verified** — `HandlerSemanticsProbe.ThrowDecorated (10) -> HandlerSemanticsProbe.InnerRaises (10) -> inner description` | chain format confirmed through the COM boundary |

## What it establishes

The guard blocks in `modProbe.bas` are hand-written copies of the exact
shape the tool emits (raise strategy), so the file doubles as a
human-readable sample of generated code.

## How to run

1. Open `HandlerSemanticsProbe.vbp` in the VB6 IDE (Project → Startup
   Object must read "(None)" — it is an ActiveX DLL, there is no
   `Sub Main`) and build it (File → Make HandlerSemanticsProbe.dll).
   If the IDE ever complains about the startup object, set it manually
   via Project → HandlerSemanticsProbe Properties → Startup Object →
   "(None)".
   All three files must keep **CRLF line endings** — VB6's project and
   class loaders misparse LF-only files (symptoms: "Sub Main() doesn't
   exist", "No creatable public component detected").
2. Register and host it from PowerShell. The DLL is 32-bit, so both the
   registrar and the host must be 32-bit, and registration needs an
   elevated prompt:

   ```powershell
   # elevated prompt; the 32-bit registrar for the 32-bit DLL
   C:\Windows\SysWOW64\regsvr32.exe .\HandlerSemanticsProbe.dll

   # the host must be 32-bit PowerShell (C:\Windows\SysWOW64\...)
   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile
   PS> $probe = New-Object -ComObject HandlerSemanticsProbe.Probe
   PS> $probe.RunProbe()
   PS> try { $probe.ThrowDecorated() } catch { $_.Exception.Message }
   PS> exit
   C:\Windows\SysWOW64\regsvr32.exe /u .\HandlerSemanticsProbe.dll
   ```

   The VB6 IDE normally **registers the DLL itself when it compiles it**
   — so try `New-Object` first; only run regsvr32 if the object cannot
   be created. If regsvr32 still fails with 0x80004005, confirm the
   prompt is elevated and that SysWOW64's regsvr32 is used (the 64-bit
   one cannot load a 32-bit in-process server).

   `New-Object` failing with `80040154 Class not registered` while the
   error text shows a resolved CLSID means the host is 64-bit
   PowerShell: the ProgID resolves, but the 32-bit DLL's CLSID lives in
   the 32-bit registry view. Run the host from
   `C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`
   (`[Environment]::Is64BitProcess` must print `False`).

   `$probe.RunProbe()` prints the fact 1–3 report;
   the `catch` prints the fact 4 message.

## How to read the results (original decision rules)

- **Fact 1 False** → decorations cannot mutate `Err.Description`; guards
  must switch to the `raise` strategy.
- **Fact 2 decorated fall-through** — `CheckDecoratedErl` returning
  `ERROR: no propagation` (observed) means fall-to-`End` does not
  re-raise: the `raise` strategy must be the default.
- **Fact 4** — with the raise strategy, expect, outermost frame first:
  `HandlerSemanticsProbe.ThrowDecorated (10) -> HandlerSemanticsProbe.InnerRaises (10) -> inner description`.
  If truncation or rewrapping occurs at the COM boundary, shorten the
  decoration format.

## Locking the result

Recorded above; `ErrorGuardPass` now defaults to
`ErrorPropagation.Raise` and the `--error-propagation` option description
documents why.
