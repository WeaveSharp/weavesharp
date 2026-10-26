# Rider API study

Rider documents a JVM UI layer and a separate ReSharper language layer.
That division supports investigating a document-backed graph, but does not
establish a working WeaveSharp integration. The [frontend trial](../../experiments/rider/README.md)
connects the shared surface to document events and a native insertion action.
Its TypeScript checks pass in the sandbox. Kotlin compilation and every IDE
journey remain pending an operator-approved Builder run.

This study uses the Builder's October 26, 2026 research notes. The linked
pages were not fetched during this run. Those notes identify Rider 2026.2.3.1,
build 262.10968.170, with 2026.3 in EAP. The build requests artifact 2026.2.3;
the installed product build must be recorded separately. The supplied notes
describe Chromium 150 in JetBrains' JCEF work, without establishing the
Chromium build actually shipped in this Rider artifact [JCEF].

Requirement references refer to private master revision
`01M5V04238N81HC7BQQW7GH0XT`, §§3, 12 and 13/ARC-03, and
layout-and-direction revision `01M5WA92KMVMDAVMDX88T5DF4A`, §§3–5.
The discussion below is independently worded.

## What the adapter needs

The graph must track unsaved source, retain the host's edit history, and
identify the selected compilation (HOST-01–02). The adapter must carry the
shared contract without inventing capabilities (HOST-03–04). Debugger work
needs session and frame identity while preserving the user's normal workflow
(HOST-05). Failure should leave ordinary source work usable (HOST-06).

Qualification must name the IDE, OS, CPU, project/runtime, language provider,
debug provider and deployment arrangement. Viewing, editing, project
intelligence, debugging, tracing and Hot Reload need separate results
(HOST-07–09). Remote work additionally needs explicit service placement,
source correspondence and recovery after disconnection (HOST-10–12, ARC-03).
Rider's inclusion in §3 is a product target, not a tested compatibility row.

## Capability record

**Documented** identifies an API primitive supported by the supplied sources.
**Limited** means that primitive addresses only part of the stated need.
**Not available** applies to this trial or to the specifically named layer.
**Needs hands-on verification** means the supplied evidence leaves the
integration unsettled. None of these statuses certifies a release.

“Remote” below means JetBrains remote development with a workspace-side IDE.
“Split” means a plugin with separately placed frontend, backend and shared
modules. The trial has no split modules or RPC interface. Its use of “frontend”
describes the IntelliJ/ReSharper division; it does not claim that its UI runs
in the remote client [RIDER, SPLIT].

| Need | Local Rider and relevant API | Remote development | Split-mode plugin |
| --- | --- | --- | --- |
| Extra graph editor beside source (HOST-01) | **documented:** `FileEditorProvider`; this trial chooses the additional-tab policy [EDITORS]. | **limited:** an unsplit plugin can render on the workspace side [SPLIT]. | **needs hands-on verification:** client placement and editor ownership [SPLIT]. |
| Embedded graph and outline | **documented:** `JBCefBrowser`, bundled JCEF dependency and `JBCefApp.isSupported()` [JCEF]. | **needs hands-on verification:** browser placement and lifetime [SPLIT]. | **needs hands-on verification:** the notes establish no JCEF split-mode contract [JCEF, SPLIT]. |
| Packaged resource responses and policy headers | **documented:** JCEF request/resource handlers and stream-response examples [LOCAL, IMAGE]. | **needs hands-on verification:** response handling where the browser runs [SPLIT]. | **needs hands-on verification:** resource ownership across the client boundary [SPLIT]. |
| Read unsaved buffers and follow changes (HOST-01) | **documented:** `FileDocumentManager`, `Document`, modification stamps and `DocumentListener` [DOC]. | **limited:** platform document mechanisms exist; synchronized client/workspace state must be exercised [SPLIT]. | **needs hands-on verification:** API placement and event order [SPLIT]. |
| Apply native text edits with undo/redo | **documented:** `WriteCommandAction`; command grouping supplies undo history [DOC, TEXT]. | **needs hands-on verification:** command execution and acknowledgement across the connection [SPLIT]. | **needs hands-on verification:** workspace transaction ownership [SPLIT]. |
| Complete graph-edit transaction protocol | **not available:** page commits are disabled in this trial. | **not available:** no remote commit path is supplied. | **not available:** no split commit path is supplied. |
| C# PSI and project structure (HOST-02) | **documented:** ReSharper's `ISolution`, `IProject`, `IProjectFile`, `IPsiSourceFile`, `IPsiModule` and PSI services [NAV, PSI]. | **limited:** these belong with the language backend; topology-specific access needs proof [RIDER, SPLIT]. | **limited:** a backend plus an explicit transport is required [RIDER, RD, SPLIT]. |
| Direct C# semantic objects in the JVM frontend | **not available:** C# PSI belongs to ReSharper [RIDER]. | **not available:** moving the UI does not expose backend objects [RIDER]. | **not available:** backend objects still require a model/transport [RIDER, RD]. |
| Exact target, linked-file ownership, references, symbols and generated inputs | **limited:** PSI/project primitives are documented; reconstructing the active compilation remains open [NAV, PSI]. | **needs hands-on verification:** confirm workspace context and target switches [SPLIT]. | **needs hands-on verification:** correlate context updates with source publications [RD, SPLIT]. |
| Shared adapter contract and truthful capability reports (HOST-03–04) | **limited:** this trial sends complete protocol-v0 sample graphs; it has no compilation service. | **not available:** no remote adapter contract is implemented here. | **not available:** no split adapter contract is implemented here. |
| Selected debug session, pause and stop events (HOST-05) | **documented:** `XDebuggerManager.TOPIC`, `XDebuggerManagerListener`, `XDebugSessionListener` and `XSourcePosition` [DEBUG]. | **limited:** the debugger session moved backend-side in 2026.1 [DEBUG]. | **needs hands-on verification:** confirm accessible listeners/proxies and their lifetime [DEBUG, SPLIT]. |
| Breakpoints and stepping owned by the IDE | **limited:** breakpoint manager, `XBreakpointListener` and .NET extension points are identified; provider behavior is untested [DEBUG, EP]. | **needs hands-on verification:** actual binding and command routing [DEBUG]. | **needs hands-on verification:** session/frame selection across the boundary [DEBUG, EP]. |
| Expression ranges, IL offsets and source/module generations | **needs hands-on verification:** a source position alone establishes no exact expression mapping [DEBUG, MAPPING]. | **needs hands-on verification:** provider precision and source correspondence [MAPPING]. | **needs hands-on verification:** generation identity across services [MAPPING]. |
| Execution history or value capture in this trial (HOST-09) | **not available:** the observer records lifecycle events and source positions only. | **not available:** no collector is supplied. | **not available:** no collector is supplied. |
| Provider-specific Hot Reload | **needs hands-on verification:** the supplied API notes establish no integration contract [EP]. | **needs hands-on verification:** qualify the provider/runtime arrangement [DEBUG]. | **needs hands-on verification:** qualify backend ownership and generations [DEBUG]. |
| Ordinary editing after view failure (HOST-06) | **limited:** the ordinary editor remains; unsupported JCEF gets a native explanation. Test failures and teardown [EDITORS, JCEF]. | **needs hands-on verification:** loss of browser or workspace connection [SPLIT]. | **needs hands-on verification:** frontend/backend restart behavior [SPLIT]. |
| UI, analysis and build placement (HOST-10, ARC-03) | **limited:** JVM/ReSharper separation and rd are documented; no analysis process or build delegation is implemented [RIDER, RD]. | **limited:** unsplit UI placement can be unsuitable for the desired client rendering [SPLIT]. | **documented:** frontend/backend/shared modules and `@Rpc` provide the placement model; the adapter still needs implementation [SPLIT]. |
| Source identity across machines (HOST-11) | **limited:** the trial uses the editor's file URL and document stamp. It does not correlate debug generations. | **needs hands-on verification:** workspace and debugger URLs may name different locations [MAPPING, SPLIT]. | **needs hands-on verification:** explicit source mappings and lifetime identity [MAPPING]. |
| Reconnect, cancellation and stale state (HOST-12) | **limited:** editor disposal removes listeners and browser resources; publications have increasing revisions. No service recovery exists. | **needs hands-on verification:** reconnect and unsent work [SPLIT]. | **needs hands-on verification:** RPC cancellation, restart and resynchronization [SPLIT]. |
| Keyboard, screen reader, theme and bidi behavior | **needs hands-on verification:** shared rendering tests are not JCEF accessibility evidence [CANVAS]. | **needs hands-on verification:** remote focus and announcements [SPLIT]. | **needs hands-on verification:** client accessibility stack [SPLIT]. |
| Layout persistence in this trial | **not available:** no layout reader or writer is connected. | **not available:** no layout transport is supplied. | **not available:** no layout transport is supplied. |
| Installation, dependencies and delivery (HOST-07–09) | **limited:** build and Marketplace routes are documented; no artifact was built or installed [BUILD, MARKET]. | **needs hands-on verification:** deployment and compatible providers [SPLIT, MARKET]. | **needs hands-on verification:** module packaging and supported combinations [SPLIT, MARKET]. |

## Document and project APIs

The frontend reads Rider's `Document` instead of reopening a source file from
disk. A `DocumentListener` rebuilds the sample graph after ordinary edits,
the native insertion, undo and redo. Each publication contains the file URL,
the modification stamp as a string, and a distinct snapshot token. A separate
graph counter prevents an older restored stamp from suppressing an undo
refresh. Changes during loading are reflected in the initial ready snapshot.
The ordinary text editor retains saving and dirty-buffer ownership [DOC].

The insertion action obtains the current editor and computes its offset
inside `WriteCommandAction.runWriteCommandAction`. It inserts an empty
statement before the supported statement containing the caret. No webview
input chooses a document, location or replacement. Multi-document edits,
stale proposals and full semantic transformations are absent. Non-text
changes would need their own `UndoableAction`/`UndoManager` integration [DOC].

On the language side, the documented route proceeds from an `IProjectFile`
through `ToSourceFile()` to `IPsiSourceFile`, then `GetPsiFile()` and
`GetPsiServices()`. `IPsiModule` represents a compilation unit [NAV, PSI].
These APIs are relevant to a later backend adapter, but do not demonstrate
that Rider exposes a Roslyn workspace or that compiler objects can be shared
with WeaveSharp. Target selection, linked source, conditional symbols,
references, generated documents and unsaved PSI synchronization each need
specific experiments. The trial's `stand-in` and `no-compiler` context fields
make that missing compilation explicit.

Rider's rd model is declared with a Kotlin DSL and generates Kotlin/C# peers
[RD]. The supplied template extends the solution model and obtains backend
SDK assets from Rider's `lib/DotNetSdkForRdPlugins` directory [TEMPLATE]. This
trial creates neither model nor backend. Split-mode `@Rpc` is another platform
boundary; it should not be treated as the same thing as the Rider rd model.

## Browser implementation and remaining uncertainty

The page is generated exclusively by SEC-04 `surfacePage`. Its HTTPS origin
is a reserved name handled inside JCEF. The packaged inventory contains only
the entry module's dependencies, the existing CSS and the generated page.
The handler matches complete URLs and GET requests without converting a
request into a filesystem path. It disables the default handler and returns
403 for all other resource requests. It admits the initial host navigation
and refuses subsequent navigation. Popups, downloads and context menus have
no offered route. No DevTools interface is added.

The generator uses the specified nonce placeholder. The host replaces it
with standard padded Base64 from 16 fresh `SecureRandom` bytes per page
response. The page policy and header policy originate from the same SEC-04
options; only the header adds `frame-ancestors 'none'`. `loadHTML` is unused,
because that route cannot supply response headers [LOCAL, IMAGE]. Runtime
header enforcement, nonce freshness in an actual browser and attempted
resource/navigation escape still require an approved JCEF session.

Host messages contain only the plugin's own publication data. UTF-8 bytes
are Base64-encoded into `window.weavesharpReceive("...")`; source text is
never interpolated into executable text. The browser uses `atob`, a fatal
`TextDecoder`, and SEC-04 `readInbound`. There is no `JBCefJSQuery`, callback
registration or outbound webview transport [QUERY]. Editing is visibly
disabled in the page. The fixed protocol-0.0 pairing performs no negotiation.

The IDE dependency uses `useInstaller = true`, addressing the documented
JCEF issue with non-installer artifacts [JCEF]. Before creating a browser,
the plugin checks `JBCefApp.isSupported()`. JCEF's documented remote-debugging
port default is 9222; this experiment changes no registry values and does
not establish that the embedding suppresses that inherited platform feature.
The Builder must account for the actual IDE configuration in its review.

## Debugger observations

The observer subscribes to `XDebuggerManager.TOPIC`, then attaches an
`XDebugSessionListener` to each session it observes. Opening a graph activates
the project service. Existing sessions are labelled attached rather than
inventing a start event. The observer stores at most 256 lifecycle/position
entries in memory and writes them to the IDE logger. A position contains a
file URL and zero-based line; absent positions stay absent. Resume and stop
entries carry no location. It never requests evaluation or records values.
There is no custom disk archive, though ordinary IDE logs can retain entries.

The 2026.1 debugger redesign moves session ownership to the backend [DEBUG].
The extension-point list identifies Rider .NET hooks without a full prose
integration guide [EP]. Neither source proves precise ranges, IL offsets,
bound-breakpoint locations or reliable source-generation correlation for
this trial. In particular, a line cannot identify one expression when several
expressions share it.

The [mapping study][MAPPING] defines the pending Debug/Release fixture work.
An approved run should record requested versus bound breakpoints, selected
sessions/frames, coordinate bases, available columns/ranges and module/source
identities. Exercise generated and hidden code, exception pauses, recursion,
threads, async continuations and iterators. Also exercise edits while paused,
missing symbols, resume and provider restart. Unknown precision stays unknown;
session events do not constitute execution history.

## Build inputs, package sources and adoption

| Input | Chosen value / source | What remains before adoption |
| --- | --- | --- |
| Gradle | 9 or later; distribution from Gradle's distribution service | Builder selects the exact release and generates a wrapper with its distribution checksum. No wrapper is included. |
| IntelliJ Platform Gradle plugin | 2.19.0 through the Gradle Plugin Portal [BUILD] | Confirm Gradle/JVM compatibility, transitive inputs and reproducible resolution. |
| Kotlin JVM plugin | 2.3.20 through the Gradle Plugin Portal | **Candidate only:** confirm the version JetBrains pairs with platform 262; the catalogue carries the same note. |
| Java toolchain | JVM 21 | Provide the JDK on each approved runner; the plugin runner's Java-17 minimum does not satisfy this chosen compilation toolchain. |
| Rider | `com.jetbrains.intellij.rider:riderRD:2026.2.3`, installer enabled [BUILD, REPOS] | Plan roughly 4 GB per Rider version before extraction and other caches; record actual artifact/build/OS/CPU. |
| Bundled JCEF and platform libraries | Rider distribution; dependency `com.intellij.modules.jcef`, plus the platform's Gson serializer [JCEF] | Confirm bundled-plugin lookup, API signatures, native runtime, Gson availability/version/terms and archive contents. |
| JUnit | Jupiter 5.11.4 and Platform launcher 1.11.4 from Maven Central | Run the Kotlin tests and review resolved transitives; no JUnit artifact was restored here. |
| Existing npm tools | Root lock: TypeScript 7.0.2, Node typings 24.19.2, VS Code typings 1.140.0 | Existing CI uses the lock. This trial adds no npm package and changes no lock. |
| Possible future backend | Rider's embedded SDK folder, or the separately licensed NuGet SDK [TEMPLATE, SDK] | Decide the backend route separately; neither is adopted by this frontend trial. |

`mavenCentral()` and `intellijPlatform { defaultRepositories() }` are the
specified Gradle repository declarations. The documented defaults involve
`cache-redirector.jetbrains.com`, JetBrains' IntelliJ repository and dependency
repository, Marketplace Maven, JetBrains Runtime and `download.jetbrains.com`
[REPOS]. Plugin resolution additionally uses the Gradle Plugin Portal.
Transitives can therefore come from more than Maven Central. No repository
was contacted and no Gradle dependency graph or lock was resolved here.

CI adoption needs an approved Gradle/JDK provisioning route, exact wrapper
selection, reviewed dependency inventories and checksums, license review,
cache capacity and supported platform runners. The current supply-chain and
dependency-register scripts concern npm/NuGet; passing them does not approve
this Gradle graph. The Builder owns any required policy/CI changes and
register regeneration. Once caches are populated, Gradle supports offline
operation [BUILD]. The sandbox did not populate them or change those policies.

The existing workflow runs root TypeScript checks after its locked npm
installation on Linux and Windows; main, scheduled and manual builds also
include macOS. The new scripts participate without a workflow change. None
of those remote jobs was triggered or observed here. The Kotlin build has
no CI integration in this change.

## Licensing and distribution

IntelliJ Platform's open-source components and the rd repository use
Apache-2.0; their presence does not relicense Rider's product distribution or
every bundled component [PLATFORM, RD]. The supplied notes describe
`JetBrains.Rider.SDK` 2026.2.3.1 as a proprietary metapackage with roughly one
hundred dependencies and plugin-development-only terms [SDK]. That package
is not a dependency of this trial. The template's embedded SDK route likewise
needs a terms review before a backend is adopted.

Gradle and Kotlin use Apache-2.0 and JUnit uses EPL-2.0 [GRADLE, KOTLIN, JUNIT].
These are tool-level license references, not an audited list of transitives
or a redistribution determination. The Builder must inspect resolved inputs
and the final archive separately. Nothing in the npm/NuGet register currently
constitutes a complete Gradle inventory.

JetBrains Marketplace is the documented eventual plugin channel. The supplied
guidance calls for Plugin Verifier results and a plugin EULA, and Marketplace
re-signs uploads [MARKET, SIGN]. A successful local build would establish
neither licensing clearance nor Marketplace approval. This work creates no
account, signing configuration, publication request or upload.

## Verification and Builder follow-up

Inside the sandbox, all five TypeScript configurations type-checked without
diagnostics. All 200 Node tests passed: 156 shared surface/canvas checks,
34 VS Code checks and ten Rider checks. Those tests check generated page
identity, nonce placement and policy agreement, the exact module closure,
schema contents, Unicode/Base64 decoding, malformed encodings, SEC-04 message
refusals, graph refresh after simulated edit/undo publications, the disabled
commit control and a clean sink scan over `experiments/rider/webview`.
VS Code test output was redirected into workspace scratch space to avoid
writing under that protected experiment. The publication tests use the
existing TypeScript stand-in as a fixture;
they do not execute Kotlin or prove Rider's edit history.

The Kotlin review covered request routing, nonce replacement, Base64 script
construction, the projection port, native write commands, document events,
debugger subscriptions and disposal. JUnit sources cover projection spans,
unsupported syntax, insertion offsets, sample size bounds, publication shape,
resource lookup, nonce substitution and message encoding. They were not run.
The Kotlin plugin version, JCEF/IDE API compatibility and the Gradle dependency
graph remain unverified. No Gradle, JDK, Rider, debugger, network restore,
cross-platform CI run or hands-on Visual Studio session was used.

After the operator approves a Builder build, the remaining checks are:

1. Confirm Kotlin/platform pairing and bundled JCEF resolution; build with the
   selected Gradle/JDK, run JUnit and Plugin Verifier, and review the archive.
2. Install on each proposed OS/CPU; verify the extra editor, module load
   timing, generated response headers, fresh nonces, refused requests and
   navigation, downloads/popups, and the inherited JCEF debugging setting.
3. Exercise unsaved changes, insertion, single-command undo/redo, source
   switching, unavailable syntax, large documents, close/reopen and failure.
4. Observe real session starts/stops, pauses, frame changes and multiple
   sessions; run the mapping fixtures in both optimization modes.
5. Check keyboard focus, screen readers, themes, bidi labels and announcements.
6. Test remote development and split mode separately, including document and
   debug-source correspondence, disconnect and restart. Do not infer their
   results from a successful local installation.
7. Review package sources/licenses, regenerate the dependency register and
   read the complete diff. Obtain the existing CI results before acceptance.

## Sources

The JetBrains research basis was supplied by the Builder. The editor guide
and tool license links provide additional review starting points. No linked
page or artifact was downloaded during this run.

- [PLATFORM] — Platform components.
- [RIDER] — Rider frontend and ReSharper backend.
- [BUILD] — Rider plugin development.
- [TEMPLATE] — Rider plugin template.
- [RD] — rd model and generator.
- [JCEF] — Embedded browser.
- [LOCAL] — Local resource handler implementation.
- [IMAGE] — Image-viewer response example.
- [QUERY] — JavaScript query implementation.
- [EDITORS] — Editor integration.
- [DOC] — Document ownership and commands.
- [TEXT] — Text editing APIs.
- [NAV] — Code model navigation.
- [PSI] — Program structure interfaces.
- [DEBUG] — Debugger architecture in 2026.1.
- [EP] — Rider extension points.
- [SPLIT] — Split mode and remote development.
- [REPOS] — Gradle repository defaults.
- [Gradle offline FAQ](https://plugins.jetbrains.com/docs/intellij/tools-intellij-platform-gradle-plugin-faq.html)
- [SDK] — Rider SDK license.
- [MARKET] — Marketplace review guidelines.
- [SIGN] — Plugin signing and Marketplace.
- [GRADLE] — Gradle license.
- [KOTLIN] — Kotlin license.
- [JUNIT] — JUnit license.
- [CANVAS] — Shared graph experiment.
- [MAPPING] — Debug mapping study.

[PLATFORM]: https://plugins.jetbrains.com/docs/intellij/intellij-platform.html
[RIDER]: https://www.jetbrains.com/help/resharper/sdk/Rider.html
[BUILD]: https://plugins.jetbrains.com/docs/intellij/rider.html
[TEMPLATE]: https://github.com/ForNeVeR/rider-plugin-template
[RD]: https://github.com/JetBrains/rd
[JCEF]: https://plugins.jetbrains.com/docs/intellij/embedded-browser-jcef.html
[LOCAL]: https://github.com/JetBrains/intellij-community/blob/master/platform/ui.jcef/jcef/utils/JBCefLocalRequestHandler.kt
[IMAGE]: https://github.com/JetBrains/intellij-community/blob/master/images/jcef/src/com/intellij/platform/images/jcef/JCefImageViewer.kt
[QUERY]: https://github.com/JetBrains/intellij-community/blob/master/platform/ui.jcef/jcef/JBCefJSQuery.java
[EDITORS]: https://plugins.jetbrains.com/docs/intellij/editors.html
[DOC]: https://plugins.jetbrains.com/docs/intellij/documents.html
[TEXT]: https://plugins.jetbrains.com/docs/intellij/working-with-text.html
[NAV]: https://www.jetbrains.com/help/resharper/sdk/NavigateCode.html
[PSI]: https://www.jetbrains.com/help/resharper/sdk/PSI_Overview.html
[DEBUG]: https://blog.jetbrains.com/platform/2026/01/platform-debugger-architecture-redesign-for-remote-development-in-2026-1/
[EP]: https://plugins.jetbrains.com/docs/intellij/rider-extension-point-list.html
[SPLIT]: https://plugins.jetbrains.com/docs/intellij/split-mode-and-remote-development.html
[REPOS]: https://plugins.jetbrains.com/docs/intellij/tools-intellij-platform-gradle-plugin-repositories-extension.html
[SDK]: https://www.nuget.org/packages/JetBrains.Rider.SDK/2026.2.3.1/License
[MARKET]: https://plugins.jetbrains.com/docs/marketplace/jetbrains-marketplace-approval-guidelines.html
[SIGN]: https://plugins.jetbrains.com/docs/intellij/plugin-signing.html
[GRADLE]: https://github.com/gradle/gradle/blob/master/LICENSE
[KOTLIN]: https://github.com/JetBrains/kotlin/blob/master/license/LICENSE.txt
[JUNIT]: https://github.com/junit-team/junit-framework/blob/main/LICENSE.md
[CANVAS]: ../../experiments/canvas/README.md
[MAPPING]: ../debugging/mapping-study.md
