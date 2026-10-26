# Projection experiment

This experiment parses one document snapshot and derives a graph for a selected
executable body. It does not save, format, compile, or execute that document. The
fixtures include deliberately unfinished code and unresolved names. This is an
M1 feasibility exercise, with no editor integration or shipped feature claim.

The design references for this work are section 5, ARC-02, and FND-01–FND-08.

## Calling it

```csharp
var snapshot = new ProjectionSnapshot("sample.cs", "buffer-17", sourceText);
var body = snapshot.Bodies().First();
var graph = snapshot.Project(body);
string json = graph.ToJson();
```

`sourceText` can be a string or Roslyn `SourceText`. The caller supplies document
and revision identities, and may supply parser options, including conditional
symbols. `Bodies()` discovers methods, constructors, operators, accessors, local
functions, lambdas, anonymous methods, and property/indexer expression bodies.
Each call projects only its selected block or expression. A nested callable's
declaration stays opaque in the outer graph; its body can be selected separately.
Constructor initializers and declaration headers are outside the selected body.

## Data and source locations

The returned graph contains immutable arrays of plain records. It contains no
syntax trees, symbols, delegates, or other Roslyn instances. JSON round trips and
graph equality compare the full ordered contents, including relationships and
cleanup routes. Element identifiers are local to one graph; they are not stable
identities across edits.

Every element carries its document, revision, syntax kind, source span and path.
Span offsets and lengths count **UTF-16 code units from the document's start**.
The path begins with `$` for the selected body. Each following integer selects a
child from `ChildNodes()`, with zero as the first index. Resolving that path in
the same parsed body recovers the node whose `Span` and kind were recorded.
Parser options and revision identity belong to the caller's snapshot context.
Paths are not relocation hints for a later revision.

`Parent` records lexical containment. `OwnsStatement` distinguishes a statement's
owner from expressions inside it. Expanded blocks have one direct owner per
statement, in source order. An opaque region owns its whole subtree; projection
does not also expand its statements. Coverage includes unreachable statements.

A C# Region stores `Source`, sliced directly from the snapshot using its anchor.
The slice includes trivia inside `Span`; leading and trailing trivia outside
that span remain in the original document. The graph is not a replacement file
format, and joining its slices is not a source reconstruction algorithm.

## What the graph shows

| Syntax | Representation |
| --- | --- |
| Blocks, ordinary declarations, expression statements, empty statements | Ordered statement owners and operand operations |
| `if` / `else` | Condition and arm regions with labelled branches |
| `while`, `do`, `for`, synchronous `foreach` | Initialization, tests, bodies, continuations and exits |
| Constant-label `switch` | Selector and sections, including several labels sharing a section |
| `try`, `catch`, filters, `finally` | Protected body, ordered handler candidates and cleanup regions |
| `using (...)`, `await using (...)`, `lock` | Acquisition/body structure and cleanup boundaries |
| `await` expressions | Operand evaluation, await boundary and logical continuation |
| `return`, `throw`, `break`, `continue` | Explicit destination and intervening cleanup scopes |
| Literals, names, member/index access, calls, ordinary unary/binary/assignment expressions | Operand order and untyped value relationships |

Relationships connect ports on anchored elements, so boundary ports need no
fabricated syntax nodes. `in` and `out` describe entry and possible normal
completion. A transfer uses a separate port, and receives no normal successor.
`for` continuations visit incrementors; `do` enters its body first. `foreach`
evaluates the collection once, then records iteration and possible disposal.
Return, break and continue destinations come from lexical nesting.

`CleanupStep` lists crossed scopes in execution order for an abrupt transfer.
The transfer element identifies the originating statement, or a try region
propagating an unhandled exception. A throw inside a protected body first reaches
that try's dispatch boundary; its finally belongs to later completion or
propagation. Handler filters and cleanup bodies remain separate regions.
Normal cleanup relationships and pending-exit routes are distinct descriptions;
they must not be flattened into unconditional outgoing edges from a shared
finally node. A cleanup that transfers control can supersede the pending exit.

These are syntax-level execution relationships, not an executable control-flow
graph. Exception candidate edges do not predict runtime matching. Potential
exceptions from individual operations, resource acquisition failures, disposal
binding, storage aliasing, overloads, inferred types, definite assignment and
constant-condition reachability are not analyzed. Value edges describe operand
uses without asserting purity, type compatibility, or reusable cached results.
Assignment and increment targets use a separate, unbound storage relationship;
this preserves the distinction between naming a destination and reading a value.
Await boundaries make no promise about suspension, scheduling or threads.

## Fallback boundary

The expression subset is deliberately small. Patterns, switch expressions,
short-circuit expressions, conditional access, queries, object/collection
creation, ref arguments, advanced jumps, iterator statements, using declarations,
async enumeration and unsafe statements are examples of opaque cases. An
unmodelled header or expression makes its containing statement opaque. Supported
regions can still contain opaque child statements.

Missing tokens, skipped input, or parser errors make the selected damaged
subtree opaque. For a damaged block body this conservatively means the entire
body, even if some statements look complete. This avoids guessing boundaries or
pretending that an unfinished construct has known execution behavior. An opaque
element's outgoing sequence means only a possible continuation; its internal
flow is unknown. Comments and inactive directive branches remain in the snapshot.

There is no semantic binding, edit generation, layout, persistence protocol,
debugger mapping, performance qualification or production language-coverage
claim in this experiment. New syntax requires explicit modelling or stays opaque.

## Validation

The tests read every fixture before and after projection, compare bytes and
text, check the syntax tree's complete text, and compare two projections.
Separate traversal resolves every anchor and checks statement ownership,
including descendants hidden by an opaque region. Additional assertions check
branch destinations, loop continuations, ordered operands, exits, cleanup order,
callable selection, parser symbols, JSON equality, BOMs, UTF-16 text and CRLF/LF.

Both projects are included in the existing solution. Package versions come from
the repository's central version file, and both projects have committed locks.
The existing CI workflow restores in locked mode and builds/tests the solution;
this change does not modify that workflow.

Sandbox results and remaining external checks are recorded with the work result.
The Builder must refresh the dependency register and review the diff before CI
or any push. The normal `dotnet test` entry point and hosted Linux, Windows and
macOS jobs require verification outside this sandbox.
