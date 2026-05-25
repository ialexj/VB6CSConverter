# VB6 to C# Converter

This is meant to be a tool to convert from VB6 to C#. This is very much a work in progress. A lot of things are missing. 

*(although what it does right now might be enough for AI to finish the migration; mileage may vary)*

## Why

I've got a large 300K LOC VB6 project that I need to migrate, and I'm too cheap to pay for the commercial offerings out there.

## How

In general, the way this works is:

1. Parse the VB6 project file and extract references and files. Create a similar destination project.
2. **Library Stage:** For each reference, retrieve library schema from the COM registry, and create thin stub classes with reference typing, property capitalization, etc.
3. **Split Stage:** Split designer output into separate files, split out large files into chunks.
4. **Transform Stage:** Parse the VB6 files using ANTLR and create a syntax tree. Then, transform that tree into a C# Roslyn syntax tree.
5. **Fixup Stage:** Run a set of rewriters to fix up the resulting code. These are things like trying to infer types, coercing literals, disambiguating array indexer calls from function calls, etc. Each rewriter builds the project, and as errors are fixed, the semantic model is improved, which usually results in more errors being exposed. The rewriters run in a loop until no more changes are possible.


