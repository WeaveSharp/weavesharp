# VS Code document experiment

This private extension connects the existing canvas to a C# `TextDocument`.
Choose **Reopen Editor With → WeaveSharp Graph Experiment** for the sample
under `test/fixtures/`. It has no default editor association.

The projection is a TypeScript stand-in. It recognizes a single class with
one parameterless `void` method, optionally `public` and/or `static`. Its
statements are integer declarations or assignments with decimal literals,
postfix increment/decrement, `return;`, and empty statements. A leading line
comment is allowed. Unknown statements produce diagnostics, interrupt the
displayed sequence and disable insertion. This grammar neither binds names
nor checks whether C# compiles. Its `complete` coverage means only that this
small grammar consumed the body. WS-05 supplies the real .NET projection;
running it in a separate service is later adapter work.

The canvas and outline come from WS-08. Arrow keys follow its logical movement
commands and transfer DOM focus to the matching `data-ws-key` element. Focus a
statement or pin, press **I** or choose **Insert empty statement**, inspect the
preview, and choose **Apply**. **Cancel** or Escape dismisses an uncommitted
preview. Other graph transformations are unavailable. Node identities use
source offsets and can change after an edit; this experiment makes no claim
of persistent graph identity. A source refresh uses WS-08's focus fallback.

An insertion adds `;` before the selected statement. It leaves existing text
in place, preserves the local indentation/newline style, and demonstrates the
transaction protocol without pretending to implement a semantic rewrite.
The host retains the proposal. It compares the commit with that proposal,
checks the editor identity and current version, then hands one text-only
`WorkspaceEdit` to `workspace.applyEdit`. Duplicate, obsolete, foreign and
changed proposals cannot apply. Failure consumes the proposal; it is never
automatically sent again. Source events publish a new graph, including events
caused by undo and redo. No graph publication writes source.

The custom text editor uses VS Code's save, dirty-buffer, backup and undo
machinery. The public edit API has no expected-version argument. The adapter
checks `document.version` immediately before constructing and submitting the
edit, without yielding; real extension-host scheduling remains part of the
integration verification. The Node history model does not prove native undo.

## Page and transport

`pageFor` delegates the complete page to SEC-04's `surfacePage`, obtaining a
new nonce with `createNonce` each time. Resource URLs come from `asWebviewUri`.
The sole origin argument is `https://*.vscode-cdn.net`. The only resource root
is `out/webview`; scripts are enabled, forms and command URIs are disabled,
and there is no port mapping. Hidden content is discarded. On hide the
adapter prepares fresh page HTML for the next reveal. Reloading also creates
a new controller identity. The Builder must check VS Code's actual hide/show
timing and resource loading.

Messages carry JSON text. The host calls `readFromSurface` and `writeToSurface`;
the browser calls `readInbound` and `writeOutbound`. All four functions, the
page generator, DOM helpers and sink scanner are imported unchanged. The
committed schema module is generated from `protocol/v0` without a fetch.

SEC-04 does not offer a surface-originated `initialize` query. Both entries
therefore use protocol 0.0 without negotiation. A fresh page sends a
`request-graph` with the fixed opening context; its editor answers with its
own context and an initialization announcement. The opening context is a
read rendezvous, never a document selector. Subsequent edits must use the
published context. The webview stores only a load counter through VS Code's
state API, so restored pages use new request identifiers. No layout state is
read or saved. General protocol-session recovery is outside this experiment.

Runtime source access is confined to the supplied `TextDocument`. The
extension has no file-opening, filesystem, child-process, network or telemetry
operation. Build scripts copy the existing stylesheet and emit module-format
markers inside `out/`; these scripts are not extension runtime code.

The debug tracker passively records pauses and stack locations already sent
by an adapter. It retains coordinate bases and missing fields, keeps sessions
separate, and drops current frames after resume, stepping, exit or a new
pause. It issues no debugger request. It cannot infer C# source identity,
IL offsets, execution history, values or precise ranges that were not sent.

## Build and offline tests

Use the dependencies already supplied in the repository. From its root:

```sh
node node_modules/typescript/bin/tsc -p src/surface
node node_modules/typescript/bin/tsc -p experiments/canvas
node node_modules/typescript/bin/tsc -p experiments/vscode/tsconfig.host.json --noEmit
node node_modules/typescript/bin/tsc -p experiments/vscode/tsconfig.webview.json --noEmit
node node_modules/typescript/bin/tsc -p experiments/vscode/tsconfig.host.json
node node_modules/typescript/bin/tsc -p experiments/vscode/tsconfig.webview.json
node experiments/vscode/copy-resources.cjs
node --test "src/surface/test/**/*.test.ts" "experiments/canvas/test/**/*.test.ts"
node --test "experiments/vscode/out/host/experiments/vscode/test/*.test.js"
```

Both programs retain strict checking and rewrite relative `.ts` imports.
The host entry and its tests emit CommonJS; imported shared sources retain
their ES module format. `copy-resources.cjs` preserves that package boundary
in the output tree for Node's supported `require(esm)` loading. The browser
program emits ES modules. All generated runtime files stay in the ignored
`experiments/vscode/out/` directory.

Regenerate the checked-in schemas only when their source contracts change:

```sh
node experiments/vscode/generate-schemas.cjs
```

The schema test compares the generated bytes with the current source schemas.
Other Node tests exercise projection offsets, edit planning, every message
boundary, transaction refusals, a simulated undo stack, browser-client state,
passive debug observations and SEC-04's sink scan. The integration entry is
compiled with the host program and excluded from the Node test glob.

The root `typecheck` script includes both programs with `--noEmit`. The root
`test` script emits both programs and runs these Node tests after the existing
surface and canvas tests. The nested manifest adds no dependency and its lock
contains only the package itself. Dependency-register regeneration belongs to
the Builder.

## Builder integration run

After the build above, use an installed VS Code version meeting the manifest's
engine range. Select a trusted fixture workspace through VS Code's normal UI;
this extension is disabled in Restricted Mode. From the repository root:

```sh
code --extensionDevelopmentPath="$PWD/experiments/vscode" --extensionTestsPath="$PWD/experiments/vscode/out/host/experiments/vscode/test/integration/index.js" --disable-extensions --user-data-dir "$PWD/.vericairn/scratch/vscode-user" "$PWD/experiments/vscode/test/fixtures"
```

This command needs no downloaded test harness. The entry exports `run()` and
uses Node assertions and VS Code's API. It opens the known fixture through
`vscode.openWith`, compares the served HTML with `surfacePage`, reloads and
checks nonce freshness across reload and hide/reveal, checks the options and
resource URLs, then drives the real handler through preview and commit. It
asserts one text-change event, dirty state, current text/version, stale refusal,
native undo/redo reasons and the republished graph at each step. It also
changes the document while a preview is pending and verifies that the preview
cannot commit. Cleanup restores and saves the fixture.

The tests inspect the extension's served page and document model. They do
not inspect the embedded browser DOM. The separate manual checklist in the
[host study](../../docs/hosts/vs-code.md) covers actual rendering, keyboard
focus, preview confirmation, hidden-page restoration, accessibility, remote
placement and debugger behavior. Run these checks per claimed host build,
OS/CPU and provider combination.

## Sandbox evidence

This run used Node 26.6.0 on macOS arm64 with the supplied TypeScript 7.0.2
and VS Code declarations 1.140.0. Both programs compile, including the
integration entry, without compiler diagnostics. The spike's Node suite passes
34 tests; another 156 pass in the shared surface and canvas suites.
The source sink scan reports no findings. The Vericairn result records the
final commit, file hashes and the final check results.

The resumed run reviewed all 27 changed files against `3d67ede`, including
the inherited work at `8d83a08`. Three regression cases failed before the
fixes: preview reuse after refusal, an inaccurate delayed-preview notice,
and loss of the pending-commit notice on a graph refresh. The final suite
covers those cases. Source review also corrected focus restoration for
preview controls and Escape handling on buttons; actual browser focus still
requires the Builder's run.

No VS Code instance or debugger was started here. Native undo/redo, browser
policy behavior, keyboard focus, assistive technology and remote placement
were not observed. No web-host entry or package archive is produced. No npm
or .NET restore, full .NET build, `dotnet test`, audit or hosted CI execution
was performed. Existing CI installs from its npm lock and uses locked .NET
restore; Linux/Windows run for pull requests, with macOS on the workflow's
other triggers. Their results, dependency-register refresh and Builder diff
review remain outstanding. No push or publication was made.
