# C# coverage inventory

`csharp-14.json` records 183 features for C# 14 and .NET 10.
The parser baseline is Microsoft.CodeAnalysis.CSharp 5.9.0.
Design references: section 6; GRF-14, GRF-15, GRF-16, GRF-17 and QUAL-03.

Every feature starts with six `unverified` assessments.
The keys are `preserve`, `render`, `dedicatedAuthoring`,
`semanticValidation`, `debugMapping` and `traceObservation`.
`plannedMaturity` describes a target interaction, not test evidence.
This inventory makes no claim that WeaveSharp implements any listed feature.

The family identifiers correspond to section 6 as follows:

| Inventory area | Family identifier |
| --- | --- |
| Source files, namespaces and imports | `compilation-and-names` |
| Type definitions and inheritance | `type-declarations` |
| Members and their modifiers | `members-and-modifiers` |
| Type shapes and signatures | `types-and-signatures` |
| Values and expression operators | `literals-and-expressions` |
| Calls, creation and initialization | `calls-and-construction` |
| Variables and matching | `variables-and-patterns` |
| Statement control flow | `structured-statements` |
| Exceptions and resource scopes | `exceptions-and-lifetime` |
| Callable bodies and captures | `callables-and-closures` |
| Awaiting and sequence traversal | `asynchrony-and-iteration` |
| Queries, expression trees and dynamic calls | `queries-and-dynamic` |
| Memory access and interoperation | `memory-and-interop` |
| Attributes, directives and compiler metadata | `compile-time-metadata` |
| Generated source and declaration provenance | `generated-and-partial` |
| Additions introduced in C# 14 | `csharp-14-additions` |

Feature identifiers remain stable when display names change.
`languageVersion` records the introduction of the represented capability.
Compiler services such as source generation use their associated C# release.
An empty `syntaxKinds` array denotes binding, context or modifier behavior
that reuses syntax already accounted for elsewhere.
Some constructs span several families; these entries are inventory units,
not an exhaustive matrix of every possible construct interaction.

`sharedSyntaxKinds` permits reuse by two or more features.
It does not count as a mapping on its own.
A classified kind cannot also appear in a feature.
Classifications cover lexical elements, trivia, directives and parser scaffolding.
An `other` entry needs a note explaining its placement.
Three preview forms in this Roslyn build use that category;
they do not extend the C# 14 baseline.
These lists are explicit snapshots; new enum names receive no automatic category.

The 576 parser names divide into 279 mapped kinds and these 297 classifications:

| Category | Count |
| --- | ---: |
| `token` | 72 |
| `keyword` | 150 |
| `trivia` | 5 |
| `structured-trivia` | 1 |
| `documentation-comment` | 36 |
| `preprocessor` | 23 |
| `list-or-placeholder` | 7 |
| `other` | 3 |

`csharp-14.schema.json` defines the file shape.
The coverage test project evaluates its local schema vocabulary, then checks
family membership, maturity, C# 14 entries and parser ownership.
Unknown schema keywords fail explicitly.
Tests compare `Enum.GetNames<SyntaxKind>()` with the stored names and report
each missing or obsolete kind. A newer named `LanguageVersion` also fails.
`Default`, `Preview`, `Latest` and `LatestMajor` are aliases, not numbered releases.
Mutation tests exercise missing mappings, added parser kinds and invalid entries.
The manifest and schema are copied beside the test assembly for portable discovery.

The solution includes this test project in the existing build workflow.
With dependencies available, run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
```

An environment that cannot launch the test coordinator can execute each
built test assembly with `dotnet` instead.
Passing these inventory checks establishes bookkeeping consistency only.
Coverage evidence for transformations, rendering or runtime behavior remains absent.
