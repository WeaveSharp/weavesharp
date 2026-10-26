# Canvas experiment evidence

The headless journeys succeed for the supported sample operations. This is evidence for command behavior and generated DOM structure. It does not establish that an editor user can complete the journey with a physical keyboard or screen reader. The host adapter, real focus transfer, source transaction and accessibility-tree behavior remain untested here.

The design references for this experiment are section 5 (GRF-04 through GRF-06), section 9 (UX-05, UX-06, UX-11 and UX-12), and layout-and-direction sections 3–5. These references identify the intended behavior; the descriptions below are specific to this implementation.

**Rendering choice and measurement.** SVG is the starting choice for the host spikes: individual shapes can retain identity, focus targets and accessible references. A bitmap renderer would still require a separate semantic view and explicit hit testing. This experiment includes that semantic view regardless. No bitmap implementation was measured, so these results do not rank SVG against canvas for speed.

Measurements were taken on 2026-10-26 with Node v26.6.0, Darwin 25.3.0, arm64. `test/benchmark.ts` constructs a chain with two execution pins per node, one enclosing region and one fewer edge than nodes. It warms each size three times and measures nine fresh recordings. The timed portion includes logical layout, descriptions, geometry, labels and the outline. Schema reading and fixture publication occur before timing. Counts include elements only unless the column says text nodes. The recorder is an in-memory object model, not a browser DOM.

| Nodes | SVG elements | Canvas view, including labels | Outline elements | Combined elements | Text nodes |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 90 | 501 | 423 | 924 | 1,173 |
| 100 | 900 | 5,001 | 4,203 | 9,204 | 11,793 |
| 1,000 | 9,000 | 50,001 | 42,003 | 92,004 | 117,993 |

| Nodes | Median milliseconds | Minimum | Maximum |
| ---: | ---: | ---: | ---: |
| 10 | 1.022 | 0.636 | 1.332 |
| 100 | 7.202 | 5.608 | 12.052 |
| 1,000 | 154.963 | 152.398 | 181.870 |

The 1,000-node case is too large to treat the current eager renderer as a proven interactive design. A browser must measure DOM attachment, style calculation, text shaping, paint, accessibility-tree updates, keyboard latency, memory and zoom/scroll behavior. Host trials should compare a visible-region strategy and a less repetitive label presentation before selecting a large-graph renderer. The single-chain workload also says little about branching, overlapping scopes or dense connections.

**Layout evidence.** Source-forward dependencies determine columns; source order breaks ties within a column. A backward connection stays backward and does not keep increasing a node's rank. The engine neither changes source order nor edits graph records. It uses integer logical coordinates and deterministic input order, without locale-sensitive sorting or random choices. Region boxes enclose their members but can overlap; this is not a constraint solver for nested scopes or edge crossings.

Let `a` be distance along flow, `c` distance across it, and `W` the total along extent. The same placement maps as follows:

| Direction | Point | Input side | Output side | Next arrow | Across-next arrow |
| --- | --- | --- | --- | --- | --- |
| Left to right | `(a, c)` | Left | Right | Right | Down |
| Right to left | `(W - a, c)` | Right | Left | Left | Down |
| Top to bottom | `(c, a)` | Top | Bottom | Down | Right |

`layout.test.ts` checks point and box mappings, exact mirror reversal, pin positions, all twelve arrow mappings, deterministic output, cycles and empty graphs. Mirroring reuses the placement map without recomputation. The live renderer uses left-to-right coordinates only. Transposition demonstrates the coordinate mapping; it does not qualify upright labels, changed node proportions, routing or host accessibility in a vertical view. Saved positions, layout-version migration and production transposition with placement hints are outside this spike.

**UX-05 coverage.** Test names below are in `test/commands.test.ts` unless another file is named.

| Concern | Evidence and boundary |
| --- | --- |
| Choose a node and its pins | `input-led connection and execution connections use the same logical commands` exercises node movement and both endpoint orientations. The insertion journey walks from node focus into its output pin. |
| Start, search and inspect a connection | `keyboard journey: insert, inspect, publish, connect and inspect the resulting relationship` searches the palette and destinations, inspects the proposal, serializes the request, then inspects a simulated publication. |
| Refuse incompatible candidates | `candidate refusals explain direction, role, type, occupancy and scope without changing focus` verifies reasons and stable focus. These are sample compatibility rules, not a C# semantic checker. |
| Commit and cancel | The journey checks focus on the requested relationship in both generated views. `filtering and cancellation retain or restore the originating pin or node` checks return to origin. The source graph stays unchanged until a publication is supplied. |
| Disconnect and reconnect | `reconnect preserves the existing edge until publication and disconnect requires a replacement` checks an explicit replacement, replacement identity and focus on the affected input. Execution disconnection is refused because this spike has no structured-control replacement editor. |
| Insert a node | The insertion journey covers a palette proposal, preview request and simulated host publication. A source location is mandatory; an empty body needs an explicit insertion span from its host. |
| Selection growth and region navigation | `source refresh cancels an obsolete proposal and separates selection from focus` checks selection expansion, region entry and membership expansion without stealing focus. |
| Delete, copy source and move execution positions | Not implemented. These require transformation-specific previews and host transactions; no test is offered as evidence for them. |
| Shortcut discoverability | Methods and entry action descriptions expose the sample commands. `layout.test.ts` verifies physical-arrow translation. Host menus, shortcut conflict handling and an ARIA-tree key adapter are still required. |
| Focus through filtering and expansion | The cancellation and source-refresh tests verify that searching and changing region membership display retain focus. Rendering records the corresponding tab stop and selected state separately. |
| Focus through source changes and removals | Removal tests cover a pin, a removed insertion origin, and a nested region with a surviving container. An empty graph falls back to its tree root. Graph switching clears old view state. |
| Recover from a refused or obsolete proposal | The host-refusal test retries through commands. Other cases cover stale source locations, missing mappings, boundary refusal, and an unrelated publication arriving while a disconnect is pending. |
| Separate selection authorities | Keyboard focus and graph selection have separate state and tests. Source selection and debugger selection are absent; host synchronization rules for those states are unverified. |

**UX-06 coverage.**

| Concern | Evidence and boundary |
| --- | --- |
| Equivalent graph content | `render.test.ts`: `outline and canvas expose all expected nodes, pins, connections and regions with identical identities` compares recorded attributes and an independently enumerated expected set, including pin owners, endpoints and region references. |
| Entry descriptions | `tree nesting, source order, selection, type, diagnostics, passing mode and actions are exposed` checks ARIA levels, groups, expansion, selection and text. Node names fall back to protocol identities and kinds because v0 provides no separate display-name field. |
| Pin descriptions | The same test checks input/output meaning, role, value type, passing mode and occupancy. The sample catalogue only models ordinary value passing and execution pins; by-reference and parameter metadata are not available. |
| Shared editing commands | Both views expose the keys consumed by one `KeyboardSession`. The insertion/connection journey checks the active key in each view, including a pending relationship. No separate outline mutation path exists. A real DOM event bridge is still needed. |
| Connection creation and conflict recovery | Command tests cover successful requests, cancellation, refusal text, source-refresh invalidation and retry. A polite status element holds the latest announcement. Actual spoken output has not been observed. |
| Text and shape beyond color | `SVG carries labelled geometry while all visible labels stay in valid HTML namespaces` checks execution diamonds, value circles, continuous versus interrupted paths, arrow geometry, role text and referenced captions. |
| Thread identities and execution states | Not implemented. Running, paused, waiting, completion, cancellation and uncertain history need runtime messages and host evidence. Static edge labels do not claim runtime observations. |
| Theme, scale, motion and focus visibility | The stylesheet uses system colors, a focus stroke and an outline. It defines no animation. Contrast, zoom, forced colors, reduced-motion settings and visible browser focus require manual or browser checks; the fake document cannot establish them. |
| Bidirectional labels | The DOM test verifies isolated label spans, displayed control markers and warnings in both views. It also rejects accidental HTML children inside SVG. Actual mixed-script shaping and announcements remain browser concerns. |
| Host-specific acceptance | Neither WS-09 nor WS-10 has been exercised here. No screen-reader claim is made for either host. |

For WS-09 (VS Code) and WS-10 (Rider), repeat the full insertion, connection, refusal, cancel, reconnect and explicit-replacement journey using the host's keyboard commands. Verify tab entry/exit, tree expansion, named pins, selected versus focused announcements, live refusal text, source conflicts, focus after removal and return from the source preview. Check the host's supported screen-reader and operating-system combinations separately. Include mixed-script labels, hidden-character warnings, zoom, high contrast and reduced motion. Record the host, OS, reader version and exact build used. Neither host should inherit acceptance from this recorder. No Visual Studio interaction was performed.

**Sandbox checks.** Both TypeScript projects pass their strict type check with no diagnostics. The experiment has 26 passing tests; the unchanged surface suite contributes 122 more. The source scan uses SEC-04's existing scanner over every file in `experiments/canvas/src` and reports zero findings. No isolation-layer implementation or configuration was changed. The valid complete and incremental protocol fixtures both enter through `readInbound`; new fixtures use the same schemas. The root test/typecheck scripts include the experiment.

No package-manager command, restore, audit, network request, .NET build or CI dispatch was run. The installed dependencies were supplied with the workspace; their restoration was not reverified. Existing CI uses locked .NET restoration and the npm lock file. It runs Linux and Windows for pull requests and additionally macOS for its other configured triggers. Those jobs were read, not executed, and their Node 24 runtime differs from this local Node 26 run. Warning-free full builds and passing checks on all three CI platforms remain required evidence. Dependency-register generation remains with the Builder.

The attempt result records the final commit, output hashes, path audit, private-content comparison and secret scan. The local private-content comparison covers the originals exported in `.vericairn/brief.md`; it cannot certify estate material that was not supplied. Scratch reports stay under `.vericairn/scratch/`. There was no push or publication.
