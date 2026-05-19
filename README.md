# VB6 to C# Converter

This is meant to be a tool to convert from VB6 to C#. This is very much a work in progress. A lot of things are missing. 

*(although what it does right now might be enough for AI to finish the migration; mileage may vary)*

## Why

I've got a large 300K LOC VB6 project that I need to migrate, and I'm too cheap to pay for the commercial offerings out there.

## How

In general, the way this works is:

1. Parse VB6 using ANTLR and create a syntax tree.
2. Walk the tree and produce a C# syntax tree using Roslyn.
3. Inspect the COM references of the VB6 project. Generate C# stubs for all the imported types as a library reference.
4. Try to build the output.
5. Run a set of rewriters to fix up the resulting code by using the semantic model.
6. Try to infer types and resolve ambiguities, like arrays vs. function calls.
7. As fixes accrue, the semantic model gets more refined, so the rewriters can do more work.
8. Go back to 3 until the code stabilizes.

## Coverage

Use the `test: coverage` task or run `pwsh -File scripts/collect-coverage.ps1` to collect Cobertura coverage data for both test projects. Reports are copied into `TestResults/Coverage/<project>/coverage.cobertura.xml`, which keeps a stable workspace path for later inspection.
