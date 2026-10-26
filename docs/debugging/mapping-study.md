# Mapping compiled locations onto the projection

The experiment maps the visible sequence points in all nine fixtures onto
WS-05 graphs and retains every hidden point as an explicit gap. It also provides
method-token/IL-offset lookup for a possible debugger location. This establishes
a source correspondence for these compilations; no live stop was observed.

The source basis is master revision `01M5V04238N81HC7BQQW7GH0XT`, section 10
and Appendix B. Relevant identifiers are DBG-01–DBG-12 and TRC-02–TRC-06,
TRC-10 and TRC-12. The descriptions here summarize the experiment's results in
new wording. The implementation is in
[experiments/debug-mapping](../../experiments/debug-mapping/README.md).

## How the measurement works

Each fixture supplies one source snapshot. Roslyn 5.9.0 parses C# 14 and compiles
that text twice, using Debug and Release optimization settings. Both runs use
the same references from the current runtime's trusted platform assembly list;
there are no configuration-specific preprocessor symbols. Emission produces
an assembly and portable PDB in memory. Warnings or compilation errors fail the
experiment. The fixture assembly is neither loaded nor executed.

The same text goes to `ProjectionSnapshot`. Its body discovery includes nested
callables, and each body gets its own projection. All nodes and regions are
represented by WS-05's `GraphElement` records, so classification iterates that
complete collection. The original graph, including its fallback regions, stays
intact. Anchor positions and PDB columns use UTF-16 units. Tests normalize
fixture newlines before both operations; a separate case exercises CRLF and a
supplementary Unicode character.

`System.Reflection.Metadata` enumerates every emitted method and all its
sequence points, including hidden entries. Records retain the method token,
IL offset, document and line/column range. Visible coordinates become absolute
snapshot spans. The smallest projected body containing the method's complete
visible source range is its candidate owner. Nested lambdas and local functions
therefore map to separate graphs. Equal-sized ambiguous candidates are refused.

State-machine links come from `GetStateMachineKickoffMethod()`. They associate
the `MoveNext` evidence with its async or iterator source body and with the
kickoff method, even when the latter has no points. The nested async local
function exercises the same mechanism. Generated lambda and local-function
method names label those associations, with source ranges supplying the body
selection. Other generated helpers are not assigned a source body merely
because their names resemble one. Method-level custom PDB records are retained
as kind identifiers and bytes; the Debug closure fixture checks for Roslyn's
lambda/closure record. The spike does not interpret closure storage or reconstruct
runtime closure objects from that blob.

Every point receives either graph targets or a reason for the missing mapping.
Equal anchors are preferred as targets; otherwise all intersecting elements in
the selected body are retained. Hidden points have no usable source range and
receive no target. A different document, ambiguous ownership, a generated helper,
or a range outside the projected body also has an explicit reason. The current
fixture corpus has only hidden points among its unmappable entries. A separate
directive test covers absent source evidence and a redirected document.

This is a controlled source-to-PDB comparison. The caller supplies the revision
label; no loaded module or external source identity is established here. Arbitrary
PDBs, source remapping directives generally, multi-document compilation, native
addresses, inlining and Hot Reload generations are outside the experiment.

## Classification precedence

Grades describe source-location support for an element. They do not describe
how faithfully an opaque region's internals are modelled, how much code ran,
or whether an expression has an inspectable value.

| First applicable condition | Grade | Recorded rule |
| --- | --- | --- |
| No method can be associated with the body | Unavailable | `NoAssociatedMethod` |
| No usable visible point intersects the element in this build | Unavailable | `NoVisibleSequencePoint` |
| This build has an intersection but the other build has none | Approximate | `OneConfigurationOnly` |
| A visible point has precisely the element's start and length | Exact | `AnchorEquality` |
| A wider point contains the element | Approximate | `SharedSequencePoint` |
| The remaining visible evidence intersects part of the anchor | Approximate | `SpanIntersection` |

Equality has priority over sharing: a whole statement can have an exact anchor
while its operand nodes share that same point and remain approximate. A broad
block, loop or cleanup region normally has only partial coverage. Its grade
does not grant independent stops to every operation it contains. An opaque
region can also have an exact source extent without gaining an internal graph.

When a point disappears in Release, its Debug-only element is downgraded even
if the Debug span equals the anchor. The absent Release element is unavailable.
The same downgrade applies to an otherwise exact point mapping. Hidden offsets
cannot rescue an unavailable element: a hidden point supplies no extent that
would justify assigning it to a specific source node.

`MapOffset` handles the emitted method-token space for one build. At a recorded
offset it returns that point's mapping. Between visible points, the preceding
range is only an approximate association. A hidden interval remains unavailable
until another visible point begins. Unknown methods, offsets beyond a method,
methods without points, and offsets before the first point have separate rules.
Interval tests include byte offsets between points to check that partition;
they do not assert that those bytes are valid instructions or real stops.

## Observed fixture results

There are 17 projected bodies containing 249 elements. Debug contributes 172
sequence points, of which 42 are hidden; Release contributes 103, with 30 hidden.
All 203 visible points have targets. All 72 hidden points carry
`HiddenSequencePoint`. There is one classification per element per build, giving
498 pinned classifications. E/A/U below means exact/approximate/unavailable.

| Fixture | Debug points (hidden) | Release points (hidden) | Debug elements E/A/U | Release elements E/A/U |
| --- | ---: | ---: | ---: | ---: |
| Straight | 7 (0) | 4 (0) | 4/22/0 | 4/20/2 |
| Branches | 12 (2) | 5 (1) | 3/18/1 | 3/18/1 |
| Loops | 38 (9) | 20 (4) | 9/41/0 | 9/39/2 |
| Switches | 24 (5) | 13 (2) | 8/19/0 | 8/17/2 |
| Cleanup | 30 (8) | 21 (6) | 7/32/0 | 7/32/0 |
| Async | 13 (7) | 12 (7) | 4/18/0 | 4/18/0 |
| Iterator | 12 (4) | 8 (3) | 4/7/0 | 4/7/0 |
| Callables | 26 (7) | 17 (7) | 9/29/2 | 9/29/2 |
| Optimization | 10 (0) | 3 (0) | 2/10/0 | 2/6/4 |
| Total | 172 (42) | 103 (30) | 50/196/3 | 50/186/13 |

These counts apply to this compiler and these sources. They are evidence for
the fixtures, not a general promise about all C# or future compiler versions.

| Construct | What the two compilations show |
| --- | --- |
| Ordinary statements and a compound calculation | Assignments and returns commonly have equal spans. The two calls, arithmetic and operands inside one calculation share a statement point. `Twice` has an exact expression-body anchor. An overwritten initialization loses its Release point. |
| `if` and `else` | The branch regions and condition operands have partial or shared coverage. Arm assignments and the final return are exact. A declaration without an initializer has no point in either build. |
| `for`, `while`, `do`, `foreach` | Loop regions remain approximate. The `for` comparison and increment each match a point; its initializer literal shares a larger range. The other loop tests have wider source points. The loop's `break` and `continue` lose separate Release points, so their Debug grade is approximate and Release grade unavailable. |
| Statement and expression switches | Sections have partial coverage. Assignments and two switch breaks retain exact spans. The final break loses its Release point. WS-05 keeps the switch-expression return opaque; that whole region has an equal point, while arm-specific points can only select the surrounding region. A selector operand also loses coverage in Release. |
| Filtered handlers and cleanup | `try`, `catch`, `finally`, `using` and `lock` regions have partial coverage. The filter operands share a larger point. Body statements have equal ranges. Compiler cleanup branches include hidden points; their presence does not identify a source cleanup edge as traversed. |
| Several awaits | The method maps through its generated state machine. Enclosing statements have exact spans, but the three await elements and their operands share those points. Seven hidden points remain in each build. A point alone cannot tell whether an await suspended. |
| Iterator | `MoveNext` links to `Count`. Yield return and yield break remain opaque WS-05 regions with exact ranges. The loop is approximate, while its test and increment are exact. Iterator suspension, disposal and resumption are not observed. |
| Lambdas and local functions | Each emitted callable has a separate body mapping. The enclosing graph has exact opaque lambda-declaration regions. Its local-function declaration regions are unavailable; points in those functions belong to their own graphs. The nested async local function is associated through its kickoff link. |
| Optimization | All four empty statements lose Release points. Debug consequently marks them with `OneConfigurationOnly`. Other retained statements still have equal spans. Method existence alone is insufficient to recover removed locations. |

WS-05 does not expose separate nodes for every expression inside an opaque
statement, switch-expression arms, iterator machinery, or implicit cleanup
instructions. This spike cannot assign an internal node that the projection
does not contain. It retains the region and its source-based grade; expanding
that language coverage is separate work.

## What observing a debugger would add

A PDB describes potential source locations in emitted IL. A live session would
show which breakpoint requests bind, move, wait or fail; which locations the
provider actually visits; and the effect of step into, over and out. JIT
optimization, inlining, exception settings, user-code filtering and provider
policy can make that behavior differ from this static inventory (DBG-01–05).

A real pause also supplies a selected execution context: a session, process,
thread and frame, plus whatever invocation or async relationship the provider
can establish. Multiple invocations can share the same token and offset.
Logical async frames and generated physical frames may be presented differently.
None of those runtime distinctions is available from these compilations alone
(DBG-06; Appendix B; TRC-06).

Host observations must establish which loaded build and source generation are
being displayed, and when edits, module replacement or Hot Reload make that
association obsolete. A matching source span in this experiment does not prove
that an editor buffer matches the executing module (DBG-07–09).

The debugger can reveal whether a local or value is available in an actual
frame and how long references remain usable. This spike captures no values and
performs no expression evaluation. Hosts must distinguish a current value from
retained display text and from absence, then expire frame-dependent state on
resume or context changes (DBG-10–12).

Neither a static point nor an observed pause establishes a completed node,
the incoming graph edge, a prior expression result, or a continuous execution
history. Those claims need additional observations with known coverage and
continuity. No trace collection, transition inference or replay is implemented
(TRC-02–05, TRC-10, TRC-12).

## Work for WS-09 and WS-10

Both host spikes need the same fixture comparison with a real debugger in
Debug and Release. Record the host, adapter/runtime versions, build, source
snapshot, requested breakpoint, actual binding, selected frame and reported
location. Then compare the observed range or offset with this study's table.
Keep missing data visible instead of filling it from an assumed capability.

| Host | Questions the spike must settle |
| --- | --- |
| WS-09: VS Code | Determine which supported debug APIs expose pauses, active frames, breakpoint changes and resume. For the chosen C# adapter, establish whether reported locations include columns, end positions, source identities and IL offsets. Test shared statement locations, hidden/generated frames, exceptions and asynchronous steps. In a remote workspace, check source URI correspondence and which process owns the relevant files and debug session. |
| WS-10: Rider | Identify the documented plugin/debugger APIs for selected sessions, frames, breakpoint binding and stepping. Establish whether the backend exposes precise ranges or only lines, how generated bodies appear, and whether source/module generations can be correlated. Test frame selection and resume across the frontend/backend boundary and record unsupported observations. |
| Both | Check zero/one-based coordinates and Unicode handling; reject an exact grade when only line granularity is available. Test overlapping calls, recursion and threads, async continuation and iterator disposal, stopped versus running state, edits during a pause, stale source, missing symbols and provider restart. Verify value lifetimes without treating evaluation as historical evidence. |

Host APIs that are merely documented still need behavioral evidence. This run
did not launch VS Code, Rider, Visual Studio or a debugger, and did not qualify
any host integration.

## Verification boundary for this run

The sandbox was macOS 26.3 on arm64 with SDK 10.0.403 and runtime 10.0.14.
The full solution restored from a workspace feed populated with existing cached
archives. A subsequent locked restore succeeded. The Release solution build
reported zero warnings and errors. All eight test executables passed directly:
720 tests, no failures and no skips, including 25 mapping tests. Every mapping
fixture is compiled at both optimization levels within those tests.

Checks include an independently enumerated PDB inventory, all 498 element
grades and rules, generated-body associations, hidden intervals, deterministic
repeat compilation, redirected documents, and source-column handling. Reports
are generated under the test build output; no generated report is a source
file. The source expectations are fixed and reviewed separately from the
report writer.

The initial restore with multiple build workers stalled and was cancelled.
A build attempted before restore completion failed for missing assets. The
single-worker restores and subsequent builds succeeded. Restore printed a
`CSSM_ModuleLoad()` diagnostic; the interrupted attempt also reported workload
verification trouble. No workload repair or download was attempted. The first
mapping run had nine expected missing-baseline failures while the tables were
being inspected; the final run passed after the expectations were added.

Final restore intermediates and build outputs use `--artifacts-path` under
`.vericairn/scratch/artifacts/`. Earlier ignored `bin/` and `obj/` directories
created during this run were removed. The final direct test runs, including
their generated reports, use those scratch artifacts. No source files outside
the assigned paths were changed.

The offline restore used the required command-line `NuGetAudit=false`; no
online advisory check is claimed. `dotnet test` was not attempted because the
brief identifies its socket requirement as unavailable in this sandbox. Direct
assembly execution verifies the tests themselves, not that orchestration path.

Linux, Windows and macOS hosted CI were neither run nor queried. Their locked
restore, build and `dotnet test` results remain to be obtained. In particular,
the existing workflow checks a generated dependency register that the Builder
must refresh for the two new projects. This change does not modify that register
or the workflow. Local macOS results cannot establish the other platforms or
the hosted macOS job.

The final commit, hashes, scope review, private-content check, secret scan and
post-commit supply-chain check are recorded in the Vericairn work result. That
record also separates the completed sandbox work from the remaining Builder
review, hosted checks and live-debugger experiments. Nothing was pushed or
published.
