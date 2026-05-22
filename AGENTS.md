# VB6Converter — Agent Navigation Guide

Tool to convert VB6 projects to C# via ANTLR parsing + Roslyn code generation.  
See [README.md](README.md) for the motivation and high-level "How" overview.

---

## Solution Structure

| Project | Type | Role |
|---|---|---|
| `VB6Parser` | Library | ANTLR4-based VB6 parser; grammar + preprocessing |
| `VB6Converter` | Console App | Conversion orchestrator; Roslyn AST generation + rewriting |
| `VB6Converter.Tests` | MSTest | Conversion integration and unit tests |
| `VB6Parser.Tests` | MSTest | Parser-level unit tests |
| `ComQuery` | Console App | Inspects COM/type-library registrations in the Windows registry |

### ComQuery tools (COM inspection)

Published self-contained executables are available at:

- `publish/ComQuery32/ComQuery.exe` — 32-bit build; reads the 32-bit COM registry hive (`HKCR\Wow6432Node`)
- `publish/ComQuery64/ComQuery.exe` — 64-bit build; reads the 64-bit COM registry hive

Use these when you need to inspect registered COM type libraries, ProgIDs, CLSIDs, or interface definitions on the local machine — for example, to determine what members a VB6 COM reference exposes before writing a conversion stub.

To rebuild the published executables:

```pwsh
dotnet publish src/ComQuery/ComQuery.csproj /p:PublishProfile=ComQuery32
dotnet publish src/ComQuery/ComQuery.csproj /p:PublishProfile=ComQuery64
```

**Target Framework**: .NET 10.0, C# latest  
**Key Dependencies**: `Antlr4.Runtime.Standard`, `Microsoft.CodeAnalysis` (Roslyn), `CommandLineParser`, `Serilog`, `Spectre.Console`, `FluentAssertions` (tests)

---

## Build & Test

When running commands, prefer `pwsh`. Do not use other console languages.

```pwsh
# Build entire solution
dotnet build VB6Converter.slnx

# Run all tests
dotnet test VB6Converter.slnx

# Run the converter
dotnet run --project VB6Converter/VB6Converter.csproj -- -p <path/to/project.vbp> -o <output_dir>
```

### CLI Options (`Program.CommandLineOptions`)

See the `Program.CommandLineOptions` class for the list of CLI command-line options.
---

## Architecture & Data Flow

```
VB6 source (.bas / .cls / .frm / .ctl)
  ↓  Preprocessor.Preprocess()
     - strips line numbers
     - joins line continuations (_)
     - splits labels off statements
  ↓  ANTLR Lexer + Parser  →  ParseContext (ANTLR AST)
  ↓  CompilationUnitConverter.GetCompilationUnit()
       └─ ClassConverter.GetClass()
            ├─ StatementConverter.GetBlock()
            ├─ DeclarationConverter.GetVariableDeclarations()
            └─ ValueConverter.GetExpression()
  ↓  Initial rewriters (applied once, no SemanticModel)
     VBLiteralRewriter · VBCoreRewriter · UsingsRewriter
     CursorRewriter · KeysRewriter · MsgBoxRewriter
  ↓  Written to output .cs file
  ─────────────────────────────────────
  ↓  ConversionWorkspace.ReloadProject()  →  Roslyn Project + Compilation
  ↓  Semantic rewriter loop (repeats until no changes)
     ControlInstanceRewriter · ForEachVariableRewriter
     TypeFinder · MemberFinder · ArrayCallDisambiguator
     TypeRefiner (cross-file variable collection)
     TypeCastRewriter · DAORewriter
     + UsingsRewriter applied after each pass
  ↓  _Diagnostics.txt written to output dir
```

Files that already exist **and have `[GeneratedCode]`** are overwritten on re-run.  
Files without `[GeneratedCode]` are skipped unless `--overwrite-user` is set.

---

## Key Files

### VB6Parser

| File | Purpose |
|---|---|
| [VB6Parser/VisualBasic6.g4](VB6Parser/VisualBasic6.g4) | ANTLR4 grammar (7000+ lines; MIT licensed from proleap-vb6-parser) |
| [VB6Parser/Preprocessor.cs](VB6Parser/Preprocessor.cs) | Normalizes raw VB6 text before lexing - gets ahead of limitations in the grammar |
| [VB6Parser/VisualBasicProject.cs](VB6Parser/VisualBasicProject.cs) | Loads `.vbp` → list of `VisualBasicProjectFile` records |
| [VB6Parser/ParseContext.cs](VB6Parser/ParseContext.cs) | Record holding ANTLR parser state (Lexer, Tokens, Parser, Source) |
| [VB6Parser/ErrorListener.cs](VB6Parser/ErrorListener.cs) | Collects `ParseError` records; throws `ParseException` on fatal errors |
| `VB6Parser/I*.cs` | Marker interfaces (`IMethodContext`, `IBlockContext`, etc.) added to ANTLR contexts |

ANTLR-generated files (`VisualBasic6Lexer.cs`, `VisualBasic6Parser.cs`) live in `obj/` — do not edit.

### VB6Converter — Orchestration

| File | Key API |
|---|---|
| [VB6Converter/Program.cs](VB6Converter/Program.cs) | `Main` → `Run(options)`: full pipeline, CLI wiring |
| [VB6Converter/VB6ToCSharpConversion.cs](VB6Converter/VB6ToCSharpConversion.cs) | `ConvertFile(input, output, …)` / `ConvertString(vb, name)` — single-file conversion |
| [VB6Converter/ConversionWorkspace.cs](VB6Converter/ConversionWorkspace.cs) | `Open()`, `ReloadProject()`, `WithCompilationUnit()` — Roslyn MSBuildWorkspace wrapper |
| [VB6Converter/ConversionTarget.cs](VB6Converter/ConversionTarget.cs) | Represents one VB6 file; tracks output path and conversion state |
| [VB6Converter/ComReference.cs](VB6Converter/ComReference.cs) | Models COM references from `.vbp` |

### VB6Converter — Conversion Visitors (`Conversion/`)

These walk ANTLR contexts and emit Roslyn `SyntaxNode`s.

| File | Converts |
|---|---|
| [CompilationUnitConverter.cs](VB6Converter/Conversion/CompilationUnitConverter.cs) | `ModuleContext` → `CompilationUnitSyntax`; applies initial rewriters; creates file-scoped namespace + class |
| [ClassConverter.cs](VB6Converter/Conversion/ClassConverter.cs) | VB6 module/class/form → C# class with `[GeneratedCode]`; form controls → base class + fields |
| [StatementConverter.cs](VB6Converter/Conversion/StatementConverter.cs) | All block statements (If, For, While, Do, Select Case, GoTo, labels, …) |
| [DeclarationConverter.cs](VB6Converter/Conversion/DeclarationConverter.cs) | `Const` / `Dim` / `Public` declarations including arrays |
| [ValueConverter.cs](VB6Converter/Conversion/ValueConverter.cs) | Expressions and literals |
| [LoopConverter.cs](VB6Converter/Conversion/LoopConverter.cs) | For/Next, Do While/Until, For Each |
| [CommonConverter.cs](VB6Converter/Conversion/CommonConverter.cs) | Shared utilities: identifier casing, VB6→C# type mapping, modifier mapping |

### VB6Converter — Rewriters (`Rewriters/`)

All rewriters extend `LoggedRewriter` (which extends `CSharpSyntaxRewriter`) and provide structured error logging.

**Initial rewriters** (no `SemanticModel` required):

| File | Role |
|---|---|
| [VBLiteralRewriter.cs](VB6Converter/Rewriters/VBLiteralRewriter.cs) | VB6 literals → C# (string escaping, numeric formats) |
| [VBCoreRewriter.cs](VB6Converter/Rewriters/VBCoreRewriter.cs) | VB6 runtime calls → C# equivalents |
| [UsingsRewriter.cs](VB6Converter/Rewriters/UsingsRewriter.cs) | Manages `using static` directives for VB runtime compat |
| [ReturnValueRewriter.cs](VB6Converter/Rewriters/ReturnValueRewriter.cs) | `FunctionName = value` → `return value` pattern |
| [ForEachVariableRewriter.cs](VB6Converter/Rewriters/ForEachVariableRewriter.cs) | ForEach loop variable fixups |
| [TryCatchRewriter.cs](VB6Converter/Rewriters/TryCatchRewriter.cs) | VB6 `On Error` → try/catch |
| [ControlInstanceRewriter.cs](VB6Converter/Rewriters/ControlInstanceRewriter.cs) | Form control singleton instances |
| [Forms/CursorRewriter.cs](VB6Converter/Rewriters/Forms/CursorRewriter.cs) | VB6 `Cursor` enum → `System.Windows.Forms` |
| [Forms/KeysRewriter.cs](VB6Converter/Rewriters/Forms/KeysRewriter.cs) | VB6 `Keys` enum → `System.Windows.Forms` |
| [Forms/MsgBoxRewriter.cs](VB6Converter/Rewriters/Forms/MsgBoxRewriter.cs) | `MsgBox` → `MessageBox.Show` |

**Semantic rewriters** (`Rewriters/Semantic/` — require compiled `SemanticModel`):

| File | Role |
|---|---|
| [TypeFinder.cs](VB6Converter/Rewriters/Semantic/TypeFinder.cs) | Infers variable types from usage patterns |
| [TypeRefiner.cs](VB6Converter/Rewriters/Semantic/TypeRefiner.cs) | Cross-file type refinement using `ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>` |
| [TypeCastRewriter.cs](VB6Converter/Rewriters/Semantic/TypeCastRewriter.cs) | Inserts explicit casts where needed |
| [MemberFinder.cs](VB6Converter/Rewriters/Semantic/MemberFinder.cs) | Resolves member accesses (properties / methods on known types) |
| [ArrayCallDisambiguator.cs](VB6Converter/Rewriters/Semantic/ArrayCallDisambiguator.cs) | Distinguishes `arr(i)` (array index) from `fn(i)` (call) |
| [DAORewriter.cs](VB6Converter/Rewriters/Semantic/DAORewriter.cs) | Data Access Object pattern rewrites |

---

## Test Conventions

Tests live in `VB6Converter.Tests/` and use **MSTest + FluentAssertions**.

### Validations helpers ([Validations.cs](VB6Converter.Tests/Validations.cs))

```csharp
// Assert the full class output matches expected C#
Validations.ValidateClassMatches(vbCode, expectedCs);

// Assert a single member (method/property/field) matches
Validations.ValidateMemberMatches(vbCode, expectedCs);

// Assert only the statements inside a method body match
// (automatically wraps vbCode in `Sub Test() ... End Sub`)
Validations.ValidateBodyMatches(vbCode, expectedCs);

// Just parse and convert — assert no errors, return the conversion
var cu = Validations.ConversionShouldSucceed(vbCode);
```

`ConversionShouldSucceed` writes the parse tree, tokens, and source to `Debug.WriteLine` on failure.

### Test file layout

| File | Tests |
|---|---|
| `CompilationTests.cs` | Full round-trip: convert → `GetCompilation()` → `Emit()` |
| `CallTests.cs` | Function/method call expressions |
| `ClassTests.cs` | Class and module declarations |
| `ForTests.cs` | For/Next loop conversions |
| `FunctionsTests.cs` | Sub/Function/Property declarations |
| `GoToTests.cs` | GoTo + label handling |
| `MsgBoxTests.cs` | MsgBox → MessageBox rewrites |
| `NewTests.cs` | `New` object instantiation |
| `RedimTests.cs` | `ReDim` / `ReDim Preserve` |
| `SwitchTests.cs` | `Select Case` → `switch` |
| `WithCallTests.cs` | `With … End With` blocks |
| `DiagnosticTests.cs` | Roslyn diagnostic assertions |
| `Rewrites/` | Rewriter-specific tests |

---

## Code Conventions

- **File-scoped namespaces**: all files use `namespace Foo;` not `namespace Foo { … }`
- **Record types for data**: `ParseError`, `ParseContext`, `VB6ToCSharpConversion`, `ConversionTarget` are records
- **`[GeneratedCode]` attribute**: all converter-output classes carry `[System.CodeDom.Compiler.GeneratedCode("VB6Converter", "1.0")]`; used to decide what can be overwritten on re-run
- **Rewriter base class**: every rewriter extends `LoggedRewriter` (not `CSharpSyntaxRewriter` directly); provides structured Serilog logging and error accumulation
- **Error files**: parse/transform errors write a `.log` file alongside each `.cs` output; `.vb6` copy of the source is written when parse errors occur
- **Global VB6 compat usings**: `_VB6Usings.cs` is generated in the output dir and provides `using static` for `FileSystem`, `Strings`, and other VB runtime members
- **VB6 file type mapping**:

  | VB6 Extension | `VisualBasicFileType` | C# Output |
  |---|---|---|
  | `.bas` | `Module` | `static` class in file-scoped namespace |
  | `.cls` | `Class` | regular class |
  | `.frm` | `Form` | class inheriting WinForms base; controls become fields |
  | `.ctl` | `Control` | class inheriting UserControl base |
