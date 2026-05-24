# VB6Converter — Agent Navigation Guide

Tool to convert VB6 projects to C# via ANTLR parsing + Roslyn code generation.  
See [README.md](README.md) for the motivation and high-level "How" overview.

---

## Solution Structure

| Project | Type | Role |
|---|---|---|
| `VB6Parser` | Library | ANTLR4-based VB6 parser; grammar + preprocessing |
| `VB6Converter` | Console App | Conversion orchestrator; Roslyn AST generation + rewriting |
| `ComStubGenerator` | Console App | Generates C# stub files from COM type libraries (used before conversion) |
| `ComQuery` | Console App | Inspects COM/type-library registrations in the Windows registry |
| `VB6Converter.Tests` | MSTest | Conversion integration and unit tests |
| `VB6Parser.Tests` | MSTest | Parser-level unit tests |
| `ComStubGenerator.Tests` | MSTest | Stub generator unit tests |
| `ComQuery.Tests` | MSTest | ComQuery integration tests |

Source projects live under `src/`; test projects live under `test/`.

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
**Key Dependencies**: `Antlr4.Runtime.Standard`, `Microsoft.CodeAnalysis` (Roslyn), `CommandLineParser`, `Serilog`, `Spectre.Console`, `AwesomeAssertions` (tests)

---

## Build & Test

When running commands, prefer `pwsh`. Do not use other console languages.

```pwsh
# Build entire solution
dotnet build VB6Converter.slnx

# Run all tests
dotnet test VB6Converter.slnx

# Run the converter
dotnet run --project src/VB6Converter/VB6Converter.csproj -- -p <path/to/project.vbp> -o <output_dir>

# Run the stub generator
dotnet run --project src/ComStubGenerator/ComStubGenerator.csproj -- -p <path/to/project.vbp> -o <output_dir>
```

### CLI Options

- **VB6Converter**: See `Program.CommandLineOptions` in [src/VB6Converter/Program.cs](src/VB6Converter/Program.cs)
- **ComStubGenerator**: See `Program.CommandLineOptions` in [src/ComStubGenerator/Program.cs](src/ComStubGenerator/Program.cs)

---

## Architecture & Data Flow

```
VB6 project (.vbp)
  ↓  VisualBasicProject.Load()  →  list of VisualBasicProjectFile + VisualBasicProjectReference
  │
  ├─ [Pre-conversion] ComStubGenerator  (skipped with --skip-stubs)
  │    ComQueryClient queries ComQuery32/64 executables
  │    LibraryMerger merges x86 + x64 results
  │    ReferenceStubGenerator writes _References/{LibName}/{TypeName}.cs stubs
  │
  ↓  Preprocessor.Preprocess()  (per source file)
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
     VBLiteralRewriter · VBCoreRewriter · KeywordEscapeRewriter · UsingsRewriter
     CursorRewriter · KeysRewriter · MsgBoxRewriter
  ↓  Written to output .cs file
  ─────────────────────────────────────
  ↓  ConversionWorkspace.ReloadProject()  →  Roslyn Project + Compilation
  ↓  Semantic rewriter loop (repeats until no changes)
     Pass 1 only: ControlInstanceRewriter · ForEachVariableRewriter
     Every pass:  TypeFinder · MemberFinder · ArrayCallDisambiguator
                  ParameterizedPropertyRewriter
                  TypeRefiner (cross-file variable collection)
                  LiteralCoercionRewriter · TypeCastRewriter
                  AmbiguousTypeQualifier
     + UsingsRewriter applied after each pass
  ↓  _Diagnostics.txt written to output dir
```

Files that already exist are overwritten on re-run.  
(Previously files without `[GeneratedCode]` were skipped, but that attribute has been removed.)

---

## Key Files

### VB6Parser (`src/VB6Parser/`)

| File | Purpose |
|---|---|
| [src/VB6Parser/VisualBasic6.g4](src/VB6Parser/VisualBasic6.g4) | ANTLR4 grammar (7000+ lines; MIT licensed from proleap-vb6-parser) |
| [src/VB6Parser/Preprocessor.cs](src/VB6Parser/Preprocessor.cs) | Normalizes raw VB6 text before lexing |
| [src/VB6Parser/VisualBasicProject.cs](src/VB6Parser/VisualBasicProject.cs) | Loads `.vbp` → list of `VisualBasicProjectFile` + `VisualBasicProjectReference` records |
| [src/VB6Parser/ParseContext.cs](src/VB6Parser/ParseContext.cs) | Record holding ANTLR parser state (Lexer, Tokens, Parser, Source) |
| [src/VB6Parser/ParseError.cs](src/VB6Parser/ParseError.cs) | `ParseError` record; `ParseException` for fatal errors |
| [src/VB6Parser/ErrorListener.cs](src/VB6Parser/ErrorListener.cs) | Collects `ParseError` records; throws `ParseException` on fatal errors |
| [src/VB6Parser/VisualBasicFileType.cs](src/VB6Parser/VisualBasicFileType.cs) | Enum: `Module`, `Class`, `Form`, `Control` |
| `src/VB6Parser/I*.cs` | Marker interfaces (`IMethodContext`, `IBlockContext`, etc.) added to ANTLR contexts |

ANTLR-generated files (`VisualBasic6Lexer.cs`, `VisualBasic6Parser.cs`) live in `obj/` — do not edit.

### VB6Converter — Orchestration (`src/VB6Converter/`)

| File | Key API |
|---|---|
| [src/VB6Converter/Program.cs](src/VB6Converter/Program.cs) | `Main` → `Run(options)`: full pipeline, CLI wiring |
| [src/VB6Converter/VB6ToCSharpConversion.cs](src/VB6Converter/VB6ToCSharpConversion.cs) | `ConvertFile(input, output, className, nsName, type)` / `ConvertString(vb, name)` — single-file conversion |
| [src/VB6Converter/ConversionWorkspace.cs](src/VB6Converter/ConversionWorkspace.cs) | `Open()`, `ReloadProject()`, `WithCompilationUnit()` — Roslyn MSBuildWorkspace wrapper |
| [src/VB6Converter/ConversionTarget.cs](src/VB6Converter/ConversionTarget.cs) | Represents one VB6 file; tracks output path and conversion state |
| [src/VB6Converter/ComReference.cs](src/VB6Converter/ComReference.cs) | Models COM references from `.vbp` |
| [src/VB6Converter/DiagnosticsReport.cs](src/VB6Converter/DiagnosticsReport.cs) | Writes `_Diagnostics.txt` to the output directory |

### VB6Converter — Conversion Visitors (`src/VB6Converter/Conversion/`)

These walk ANTLR contexts and emit Roslyn `SyntaxNode`s.

| File | Converts |
|---|---|
| [CompilationUnitConverter.cs](src/VB6Converter/Conversion/CompilationUnitConverter.cs) | `ModuleContext` → `CompilationUnitSyntax`; applies initial rewriters; creates file-scoped namespace + class |
| [ClassConverter.cs](src/VB6Converter/Conversion/ClassConverter.cs) | VB6 module/class/form → C# class; form controls → base class + fields |
| [StatementConverter.cs](src/VB6Converter/Conversion/StatementConverter.cs) | All block statements (If, For, While, Do, Select Case, GoTo, labels, …) |
| [DeclarationConverter.cs](src/VB6Converter/Conversion/DeclarationConverter.cs) | `Const` / `Dim` / `Public` declarations including arrays |
| [ValueConverter.cs](src/VB6Converter/Conversion/ValueConverter.cs) | Expressions and literals |
| [LoopConverter.cs](src/VB6Converter/Conversion/LoopConverter.cs) | For/Next, Do While/Until, For Each |
| [CommonConverter.cs](src/VB6Converter/Conversion/CommonConverter.cs) | Shared utilities: identifier casing, VB6→C# type mapping, modifier mapping |
| [CallContext.cs](src/VB6Converter/Conversion/CallContext.cs) | `CallContext` (tracks `With` block receiver) and `ClassContext` records |
| [TransformErrors.cs](src/VB6Converter/Conversion/TransformErrors.cs) | Attaches/reads `TransformError` annotations on `SyntaxNode`s |

### VB6Converter — Rewriters (`src/VB6Converter/Rewriters/`)

All rewriters extend `LoggedRewriter` (which extends `CSharpSyntaxRewriter`) and provide structured Serilog logging.

**Initial rewriters** (no `SemanticModel` required):

| File | Role |
|---|---|
| [VBLiteralRewriter.cs](src/VB6Converter/Rewriters/VBLiteralRewriter.cs) | VB6 literals → C# (string escaping, numeric formats) |
| [VBCoreRewriter.cs](src/VB6Converter/Rewriters/VBCoreRewriter.cs) | VB6 runtime calls → C# equivalents |
| [KeywordEscapeRewriter.cs](src/VB6Converter/Rewriters/KeywordEscapeRewriter.cs) | Prefixes identifiers that clash with C# keywords with `@` |
| [UsingsRewriter.cs](src/VB6Converter/Rewriters/UsingsRewriter.cs) | Manages `using static` directives for VB runtime compat |
| [ReturnValueRewriter.cs](src/VB6Converter/Rewriters/ReturnValueRewriter.cs) | `FunctionName = value` → `return value` pattern |
| [ForEachVariableRewriter.cs](src/VB6Converter/Rewriters/ForEachVariableRewriter.cs) | ForEach loop variable fixups |
| [TryCatchRewriter.cs](src/VB6Converter/Rewriters/TryCatchRewriter.cs) | VB6 `On Error` → try/catch |
| [ControlInstanceRewriter.cs](src/VB6Converter/Rewriters/ControlInstanceRewriter.cs) | Form control singleton instances |
| [Forms/CursorRewriter.cs](src/VB6Converter/Rewriters/Forms/CursorRewriter.cs) | VB6 `Cursor` enum → `System.Windows.Forms` |
| [Forms/KeysRewriter.cs](src/VB6Converter/Rewriters/Forms/KeysRewriter.cs) | VB6 `Keys` enum → `System.Windows.Forms` |
| [Forms/MsgBoxRewriter.cs](src/VB6Converter/Rewriters/Forms/MsgBoxRewriter.cs) | `MsgBox` → `MessageBox.Show` |

**Semantic rewriters** (`src/VB6Converter/Rewriters/Semantic/` — require compiled `SemanticModel`):

| File | Role |
|---|---|
| [TypeFinder.cs](src/VB6Converter/Rewriters/Semantic/TypeFinder.cs) | Infers variable types from usage patterns |
| [TypeRefiner.cs](src/VB6Converter/Rewriters/Semantic/TypeRefiner.cs) | Cross-file type refinement using `ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>` |
| [TypeCastRewriter.cs](src/VB6Converter/Rewriters/Semantic/TypeCastRewriter.cs) | Inserts explicit casts where needed |
| [LiteralCoercionRewriter.cs](src/VB6Converter/Rewriters/Semantic/LiteralCoercionRewriter.cs) | Coerces bare numeric literals to `bool`/`decimal`/`float` where the LHS type demands it |
| [MemberFinder.cs](src/VB6Converter/Rewriters/Semantic/MemberFinder.cs) | Resolves member accesses (properties / methods on known types) |
| [ArrayCallDisambiguator.cs](src/VB6Converter/Rewriters/Semantic/ArrayCallDisambiguator.cs) | Distinguishes `arr(i)` (array index) from `fn(i)` (call) |
| [ParameterizedPropertyRewriter.cs](src/VB6Converter/Rewriters/Semantic/ParameterizedPropertyRewriter.cs) | Rewrites `obj.Foo[k] = v` element-access assignments to `obj.SetFoo(k, v)` calls |
| [AmbiguousTypeQualifier.cs](src/VB6Converter/Rewriters/Semantic/AmbiguousTypeQualifier.cs) | Fully qualifies type names that are ambiguous across multiple `using` namespaces |
| [DAORewriter.cs](src/VB6Converter/Rewriters/Semantic/DAORewriter.cs) | Data Access Object pattern rewrites *(currently disabled)* |

### ComStubGenerator (`src/ComStubGenerator/`)

Generates C# stub source files from COM type libraries so that VB6 COM references compile in the converted project.

| File | Role |
|---|---|
| [src/ComStubGenerator/Program.cs](src/ComStubGenerator/Program.cs) | CLI entry point; orchestrates stub generation for a `.vbp` or an explicit library list |
| [src/ComStubGenerator/ComQueryClient.cs](src/ComStubGenerator/ComQueryClient.cs) | Shells out to `ComQuery32.exe` / `ComQuery64.exe` and deserializes results |
| [src/ComStubGenerator/LibraryMerger.cs](src/ComStubGenerator/LibraryMerger.cs) | Merges x86 + x64 `ComQueryLibrary` collections by GUID; prefers most-specific member types |
| [src/ComStubGenerator/ReferenceStubGenerator.cs](src/ComStubGenerator/ReferenceStubGenerator.cs) | Writes `_References/{LibName}/{TypeName}.cs` stubs from a `ComQueryLibrary` |
| [src/ComStubGenerator/ReferenceUsingsGenerator.cs](src/ComStubGenerator/ReferenceUsingsGenerator.cs) | Emits a global `using` file for alias types collected across all libraries |
| [src/ComStubGenerator/SyntheticMembersLoader.cs](src/ComStubGenerator/SyntheticMembersLoader.cs) | Loads `synthetic_members.json` — hand-authored member overrides injected into stubs |
| [src/ComStubGenerator/SyntheticMembersApplicator.cs](src/ComStubGenerator/SyntheticMembersApplicator.cs) | Applies synthetic member definitions on top of generated stubs |

---

## Test Conventions

Tests use **MSTest + AwesomeAssertions**.

### VB6Converter.Tests — Validations helpers ([test/VB6Converter.Tests/Validations.cs](test/VB6Converter.Tests/Validations.cs))

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

### VB6Converter.Tests file layout

Root-level tests:

| File | Tests |
|---|---|
| `CompilationTests.cs` | Full round-trip: convert → `GetCompilation()` → `Emit()` |
| `CallTests.cs` | Function/method call expressions |
| `ConversionTargetTests.cs` | `ConversionTarget` path/state logic |
| `DiagnosticTests.cs` | Roslyn diagnostic assertions |

`Conversion/` subfolder:

| File | Tests |
|---|---|
| `ClassTests.cs` | Class and module declarations |
| `ForTests.cs` | For/Next loop conversions |
| `FunctionsTests.cs` | Sub/Function/Property declarations |
| `GoToTests.cs` | GoTo + label handling |
| `IOTests.cs` | File I/O statement conversions |
| `NewTests.cs` | `New` object instantiation |
| `RedimTests.cs` | `ReDim` / `ReDim Preserve` |
| `StatementTests.cs` | Miscellaneous statement conversions |
| `SwitchTests.cs` | `Select Case` → `switch` |
| `ValueTests.cs` | Expression and value conversions |
| `WithCallTests.cs` | `With … End With` blocks |

`Rewrites/` subfolder:

| File | Tests |
|---|---|
| `AmbiguousTypeQualifierTests.cs` | `AmbiguousTypeQualifier` rewriter |
| `DisambiguatorTests.cs` | `ArrayCallDisambiguator` rewriter |
| `KeywordEscapeRewriterTests.cs` | `KeywordEscapeRewriter` |
| `LiteralCoercionTests.cs` | `LiteralCoercionRewriter` |
| `MsgBoxTests.cs` | `MsgBoxRewriter` |
| `RecordsetRewriterTests.cs` | Recordset-specific rewrites |
| `VBCoreRewriterTests.cs` | `VBCoreRewriter` |
| `VBLiteralRewriterTests.cs` | `VBLiteralRewriter` |

### ComStubGenerator.Tests file layout

| File | Tests |
|---|---|
| `ReferenceStubGeneratorTests.cs` | Stub file generation from `ComQueryLibrary` |
| `LibraryMergerTests.cs` | x86/x64 library merge logic |
| `SyntheticMembersTests.cs` | Synthetic member loading and application |
| `DotnetStubGeneratorTests.cs` | .NET type stub generation |

---

## Code Conventions

- **File-scoped namespaces**: all files use `namespace Foo;` not `namespace Foo { … }`
- **Record types for data**: `ParseError`, `ParseContext`, `VB6ToCSharpConversion`, `ConversionTarget` are records
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
