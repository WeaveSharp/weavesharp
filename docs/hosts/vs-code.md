# VS Code host study

VS Code documents the primitives needed for a document-backed graph view:
a custom text editor, a webview, text transactions and document-change events.
The [experiment](../../experiments/vscode/README.md) connects these primitives
through SEC-04 and exercises a small insertion. This establishes a buildable
adapter and offline behavior tests. It does not qualify a VS Code installation
or a remote configuration.

The evidence basis is the Builder's research dated October 26, 2026, plus
local inspection of `@types/vscode` 1.140.0. The source links below identify
those references; this sandbox did not fetch them. The supplied installed
toolchain uses TypeScript 7.0.2 and Node 26.6.0. The research notes identify
VS Code 1.143.1, Electron 43.7.7, Chromium 150 and Node 24.21.0 as the current
desktop combination. That combination was not launched here.

Requirement references identify private master revision
`01M5V04238N81HC7BQQW7GH0XT`, §§3 and 12, HOST-01–HOST-12, and §13/ARC-03.
The presentation follows layout-and-direction §§3–5. The descriptions here
are original summaries. HOST-01 needs live source and native edit history;
HOST-02 needs compilation context beyond a filename. HOST-03–04 concern a
common adapter contract and honest capability reporting. HOST-05–06 require
debugger ownership and usable ordinary editing after failure. HOST-07–09
separate configuration, licensing and feature qualification. HOST-10–12 and
ARC-03 concern service placement, source identity and recovery.

## Document model and edit contract

`CustomTextEditorProvider` uses the existing text document. VS Code manages
saving, dirty state, backup and undo; the provider redraws after
`onDidChangeTextDocument` [CE, API]. That event also covers the extension's
edits and undo/redo. This fits a source-authoritative graph better than a
`CustomDocument`, whose provider must supply its own save/revert/backup logic
and undo/redo callbacks [API]. The optional `weavesharp.graph` contribution
uses `*.cs`; ordinary C# editing remains the default.

The experiment prepares an empty-statement insertion, displays its offset
and replacement text, and waits for an explicit commit. The host stores the
proposal and compares the supplied context, document snapshots and changes
with the current model and that proposal. It then creates one `WorkspaceEdit`
and calls `workspace.applyEdit` once. A text-only edit has atomic application
semantics [API]. A false result or exception produces a refusal without a
retry. Native source-change events publish the graph afresh.

The public API exposes a monotonically increasing document version, including
across undo/redo, but `WorkspaceEdit` has no expected-version parameter [API].
The adapter therefore checks immediately before submission, with no async gap
inside that adapter call. Node tests prove that obsolete requests do not reach
the document operation and that the adapter boundary can refuse a version
change. The integration test must establish native undo grouping and actual
host behavior; a distributed compare-and-swap guarantee is not established by
these declarations alone.

The TypeScript projection is explicitly a stand-in. It recognizes a tiny
statement grammar, uses UTF-16 source spans and emits sample palette nodes
in source order. It does not resolve types, references, linked-file ownership
or target frameworks. WS-05's .NET implementation is the projection intended
for later service integration. This spike starts no such service.

## Capabilities by environment

**Documented** means the cited API provides the named primitive.
**Limited** means the primitive covers only part of the need or depends on
another provider. **Not available** means this implementation or host model
does not offer the named operation. **Needs hands-on verification** means the
available material cannot establish the actual integration. None of these
labels is a release support claim.

“Local” means Microsoft's desktop VS Code. The four remote columns assume a
connected workspace and its Node extension host. Codespaces includes a
browser frontend connected to that host. The last column means standalone
`vscode.dev` without a remote execution host; it is a different arrangement.
The remote placement basis is [REMOTE, HOST]; browser restrictions come from
[WEB]. Statuses describe the named need, not everything in a requirement.

| Need | Local | Remote-SSH | WSL | Dev Containers | Codespaces | Standalone vscode.dev |
| --- | --- | --- | --- | --- | --- | --- |
| Custom text editor and webview primitives (HOST-01) [CE, WG] | documented | documented | documented | documented | documented | documented |
| Unsaved text and document lifecycle (HOST-01) [API] | documented | documented | documented | documented | documented | documented |
| Text transaction, undo and redo primitives (HOST-01) [CE, API] | documented | documented | documented | documented | documented | documented |
| Expected-version transaction guarantee (HOST-01) [API] | limited | limited | limited | limited | limited | limited |
| Run this Node-only spike as supplied [HOST, WEB] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | not available |
| Place extension logic beside workspace files (HOST-10) [REMOTE, HOST] | documented | documented | documented | documented | documented | not available |
| Host the future .NET analysis service (ARC-03) [HOST, WEB] | limited | limited | limited | limited | limited | not available |
| Full active C# project/compiler context (HOST-02) [COMMANDS, CSHARP] | limited | limited | limited | limited | limited | limited |
| Symbols, navigation and provider code actions (HOST-02–03) [COMMANDS] | limited | limited | limited | limited | limited | limited |
| Resource and workspace URI APIs (HOST-11) [WG, REMOTE, VIRTUAL] | documented | documented | documented | documented | documented | documented |
| Match a debugger source to the exact workspace snapshot (HOST-11) [API, MAPPING] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification |
| Selected debug session/frame and breakpoint events (HOST-05) [API] | documented | documented | documented | documented | documented | limited |
| Passive adapter-message observation (HOST-05) [API] | documented | documented | documented | documented | documented | limited |
| C# adapter precision and source-generation correspondence [MAPPING, DEBUG-LICENSE] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | not available |
| Generic recorded execution history from these APIs (HOST-09) [API] | not available | not available | not available | not available | not available | not available |
| Provider-specific Hot Reload integration (HOST-09) [API, CSHARP] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | not available |
| This spike in Restricted Mode [TRUST] | not available | not available | not available | not available | not available | not available |
| This spike in a virtual workspace [VIRTUAL] | not available | not available | not available | not available | not available | not available |
| Layout file persistence in this spike | not available | not available | not available | not available | not available | not available |
| Host isolation behavior for the supplied page [WG, WEBVIEW-SOURCE] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification |
| Keyboard, screen-reader and theme acceptance (UX-05–06) [WG, CANVAS] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification |
| Disconnect, restart, cancellation and late replies (HOST-06, HOST-12) [REMOTE, HOST] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification |
| Qualified install/update and provider combination (HOST-07–09) [PUBLISH, CSHARP] | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification | needs hands-on verification |

The provider-command row is limited because commands dispatch to installed
providers. They do not guarantee a C# provider is present or expose its full
compilation. The standalone browser debug rows depend on a suitable web
adapter; the native C# runtime/debugger route described in the supplied notes
does not run there. A connected Codespace can supply that remote execution
environment, but its provider, runtime and source mappings still need tests.
Hot Reload is left unqualified because no general integration contract was
established in the supplied API evidence. Missing documentation is not proof
that a particular C# extension lacks an interactive feature.

## Webview, trust and lifecycle

The manifest chooses `extensionKind: ["workspace"]`, a CommonJS `main`, and
no `browser` entry. Under SSH, WSL, containers or Codespaces, its extension
logic belongs in the workspace host while the webview renders at the client
[REMOTE, HOST]. Browser-only extensions have no Node process facilities and
need a browser-compatible entry; the documented packaging guidance calls for
a bundled web entry [WEB]. No such delivery is built here.

The page is entirely SEC-04 output. Each generation calls `createNonce` then
`surfacePage`. Its resource origins are exactly `["https://*.vscode-cdn.net"]`;
script and stylesheet URLs use `asWebviewUri`. Local roots contain only the
compiled webview tree. The adapter sets scripts on, forms off, command URIs
off, retained hidden content off, and no port mapping. Hidden views receive
newly generated HTML for their next reveal. No raw markup or additional
policy is authored by the spike. The Builder must observe actual restoration
timing, browser enforcement and VS Code's injected bootstrap script together.

The boundary uses JSON strings through `readFromSurface`/`writeToSurface` on
the host and `readInbound`/`writeOutbound` in the browser. The renderer creates
elements through SEC-04. Embedded generated schemas avoid runtime loading.
The runtime neither opens paths from messages nor reads layout files. The
custom editor's own document is its only source model. There are no process,
network or telemetry calls. Future layout containment belongs to SEC-05.

The supplied source notes describe JSON serialization rather than structured
clone and warn that successful `postMessage` means posting, not receipt
[MESSAGING, API]. Hidden-view messaging also differs between the guide and
API notes. The experiment relies on a fresh page request after restoration,
not hidden-message delivery. Lost mutation replies are not retried. Full
session reconciliation and reconnect behavior remain unfinished adapter work.

Protocol 0.0 is fixed for this pair of entries. The current SEC-04 surface
allow-list omits an `initialize` query. A page therefore requests its first
graph with a fixed opening context, and the host announces its identity and
answers using the editor-owned document. This is a narrow startup convention,
not a completed version-negotiation implementation. The opening identifier
never authorizes an edit; only the published current context can do that.

Trusted Types behavior inside the embedded frame is not established by the
workbench's own policy. The notes also identify no desktop webview response
header carrying CSP or `frame-ancestors` [WEBVIEW-SOURCE]. Those observations
do not authorize a policy change here; they are hands-on questions for the
Builder's SEC-04 work. The existing isolation module stays unchanged.

Restricted Mode disables this extension through its manifest [TRUST]. The
manifest also declines virtual workspaces [VIRTUAL]. Remote workspaces are
not synonymous with virtual workspaces. A future adapter must use host URIs
and `workspace.fs` when it needs workspace files, and cannot treat a remote
URI as a client-side filesystem path. This spike requires no runtime file IO.

## Debugger observations and remaining mapping work

`vscode.debug` exposes session start/termination, breakpoint changes, the
active stack item and changes to that selection. A focused frame is available
only during a pause after stack retrieval; a focused thread is not itself
proof of a pause [API]. `SourceBreakpoint` and `addBreakpoints` provide the
request mechanism, not evidence of the adapter's actual binding precision.
`registerDebugAdapterTrackerFactory` can observe adapter traffic, while
`DebugSession.customRequest` is a provider-specific request route [API].

The experiment registers a passive tracker per session. It observes `stopped`
events and matches existing `stackTrace` responses to requests made during
that pause. It records source name/path/reference and line, column and end
positions when present. Initialize arguments determine coordinate bases;
missing locations remain missing. Resume, step requests, termination and a
new pause retire current frame observations. It neither asks the debugger for
frames nor controls selection, breakpoints or stepping. It does not collect
values or historical execution and does not claim an IL-offset channel.

These answers cover the API portion of [MAPPING]'s WS-09/WS-12 investigation:

| Question | Evidence status and next observation |
| --- | --- |
| Pauses, resume and session separation | documented tracker events and session IDs; Node tests exercise invalidation. Verify the chosen adapter's actual sequence. |
| Active frame and breakpoint changes | documented APIs [API]; not wired into a graph overlay or exercised with a debugger here. |
| Source identity, columns and end positions | limited to fields the adapter sends. Verify path/URI correspondence, coordinate bases and UTF-16 handling in every topology. |
| IL offsets and precise expression mapping | needs hands-on verification; no general IL-offset access was established. A line alone cannot justify an exact node match. |
| Shared source locations, hidden/generated code and optimization | needs hands-on verification with the Debug/Release fixtures in [MAPPING]. |
| Exception pauses, async continuations, iterators and recursive/concurrent calls | needs hands-on verification; pause/thread/frame IDs do not prove invocation history. |
| Edits while paused, missing symbols and adapter restart | needs hands-on verification of generation correspondence and stale-data retirement. |
| Captured values, tracing and Hot Reload | not supplied by this observer. Evaluate each provider facility independently; evaluation is not recorded history. |

## Providers and distribution

The C# extension supplies no documented inter-extension API for its Roslyn
workspace in the research basis. Its experimental request export is not a
foundation for this adapter. Documented `vscode.execute*Provider` commands
can request symbols, definitions, references, actions and formatting, subject
to provider availability [COMMANDS]. Effective targets, symbols, references
and linked documents still need a separately supported project-context route.

The supplied C# runtime terms restrict the included binaries to Microsoft's
VS Code, Visual Studio and Xamarin Studio [CSHARP]. The cited vsdbg terms
name Microsoft's VS Code, Visual Studio and Visual Studio for Mac
[DEBUG-LICENSE]. These distinct terms must be reviewed for the actual
artifacts; they do not grant redistribution or compatible-editor use.
WeaveSharp's license does not change another vendor's terms.

An eventual VSIX can have a Marketplace route and a separately reviewed Open
VSX route [PUBLISH, OPENVSX]. API compatibility, marketplace availability and
the right to distribute C# tooling are separate decisions. Cursor, Windsurf,
VSCodium and Google Antigravity need their own provider and install evidence
under §3; Theia and code-server remain later investigations. No compatible
editor inherits Microsoft's C# debugger rights from this experiment.

The private manifest is version `0.0.0`, targets `^1.140.0`, and adds no
dependency. The engine lower bound matches the supplied declaration version.
The notes identify vsce 4.0.0 and its much larger package graph; neither a
packager nor a test-harness dependency is added. Packaging, artifact review,+Marketplace/Open VSX access and publication wait for later work. The Builder
regenerates the dependency register; this change does not edit it.

## Builder qualification checklist

1. Run the compiled integration entry using the exact command in the
   experiment README. Record editor build, platform/CPU and extension commit.
   Verify actual undo/redo, the single transaction and the fresh graph versions.
2. Observe the loaded canvas and outline. Exercise arrows, preview, Apply,
   Cancel, source edits, focus fallback, tab entry/exit and hide/reveal. Confirm
   new page nonces, resource loading and the unchanged isolation layer's behavior.
3. Test screen readers, mixed-script labels, hidden-character announcements,
   high contrast, zoom and reduced-motion settings on each claimed platform.
   Only left-to-right graph rendering is offered here.
4. Repeat the document journey under SSH, WSL, containers and Codespaces.
   Record where the extension runs, the document/resource URIs, disconnects,
   reconnects, stale messages and dependency failures. Test both desktop and
   browser clients for Codespaces; standalone vscode.dev is unsupported here.
5. Use the mapping fixtures with a permitted C# debugger in Debug and Release.
   Compare requested/bound breakpoints, selected sessions/frames, source ranges,
   async/exception behavior and missing data. Record actual provider/runtime
   versions and source generations. Keep viewing, authoring, project services,
   debugging, tracing and Hot Reload as separate qualification results.
6. Review every diff, regenerate the dependency register, and collect hosted
   checks for Linux, Windows and macOS with the existing lock-based restore.
   Local tests do not replace those CI results or the private-content gate.

The sandbox compiled both TypeScript programs and the integration entry,
and ran the Node suites. It did not launch VS Code, a browser, any debugger,
or a remote host; perform a package restore or .NET build/test; dispatch CI;
or make a package/push/publication. The final Vericairn result supplies the
commit, hashes, exact check outcomes and remaining verification boundaries.

## Sources

- **CE:** [Custom editors](https://code.visualstudio.com/api/extension-guides/custom-editors).
- **API:** [VS Code API](https://code.visualstudio.com/api/references/vscode-api); the supplied `node_modules/@types/vscode/index.d.ts` at 1.140.0 was inspected locally.
- **WG:** [Webview guide](https://code.visualstudio.com/api/extension-guides/webview).
- **REMOTE:** [Remote extensions](https://code.visualstudio.com/api/advanced-topics/remote-extensions).
- **HOST:** [Extension hosts](https://code.visualstudio.com/api/advanced-topics/extension-host).
- **WEB:** [Web extensions](https://code.visualstudio.com/api/extension-guides/web-extensions).
- **VIRTUAL:** [Virtual workspaces](https://code.visualstudio.com/api/extension-guides/virtual-workspaces).
- **TRUST:** [Workspace trust](https://code.visualstudio.com/api/extension-guides/workspace-trust).
- **COMMANDS:** [Provider and editor commands](https://code.visualstudio.com/api/references/commands).
- **CSHARP:** [C# runtime license](https://github.com/dotnet/vscode-csharp/blob/main/RuntimeLicenses/license.txt), with the Builder's notes on the absence of a supported workspace export.
- **DEBUG-LICENSE:** [C# debugger license](https://github.com/dotnet/vscode-csharp/blob/main/docs/debugger/Microsoft-.NET-Core-Debugger-licensing-and-Microsoft-Visual-Studio-Code.md).
- **MESSAGING:** [Extension-host webview messaging](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/api/common/extHostWebviewMessaging.ts).
- **WEBVIEW-SOURCE:** Builder source inspection references: [webview definitions](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/contrib/webview/common/webview.ts), [host webview](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/api/common/extHostWebview.ts), [inner frame](https://github.com/microsoft/vscode/blob/main/src/vs/workbench/contrib/webview/browser/pre/index.html), [workbench page](https://github.com/microsoft/vscode/blob/main/src/vs/code/electron-browser/workbench/workbench.html), and [desktop protocol provider](https://github.com/microsoft/vscode/blob/main/src/vs/platform/webview/electron-main/webviewProtocolProvider.ts). These are moving references; no upstream commit was supplied.
- **PUBLISH:** [Publishing extensions](https://code.visualstudio.com/api/working-with-extensions/publishing-extension) and [vsce manifest validation](https://github.com/microsoft/vscode-vsce/blob/main/src/validation.ts). The publishing guide is a follow-up reference, not a page fetched here.
- **OPENVSX:** [Open VSX publishing](https://github.com/eclipse/openvsx/wiki/Publishing-Extensions), a follow-up distribution reference not inspected in this run.
- **MAPPING:** [Debugger mapping study](../debugging/mapping-study.md), especially its work for WS-09 and WS-10.
- **CANVAS:** [Canvas evidence](../../experiments/canvas/EVIDENCE.md), including its remaining browser and accessibility checks.
