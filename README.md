# VB6 to C# Converter

This is meant to be a tool to convert from VB6 to C#. This is very much a work in progress. A lot of things are missing. 

*(although what it does right now might be enough for AI to finish the migration; mileage may vary)*

## Why

I've got a large 300K LOC VB6 project that I need to migrate, and I'm too cheap to pay for the commercial offerings out there.

## How

In general, the way this works is:

1. Parse VB6 using ANTLR and create a syntax tree.
2. Walk the tree and produce a C# syntax tree using Roslyn.
3. Try to build the output.
4. Run a set of rewriters to fix up the resulting code by using the semantic model.
5. Try to infer types and resolve ambiguities, like arrays vs. function calls.
6. Attempt to generate stub classes for missing types and dependencies.
8. As fixes accrue, the semantic model gets more refined, so the rewriters can do more work.
9. Go back to 3 until the code stabilizes.
