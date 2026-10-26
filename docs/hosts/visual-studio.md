# Visual Studio host study

Visual Studio remains a candidate for WeaveSharp. The documented
VisualStudio.Extensibility command and Remote UI APIs are enough to define a
small build experiment. They do not establish that the complete graph editor
can use that extension model. The strongest next candidate for the host adapter
is the in-process, VSSDK-compatible model, subject to the checks below.

This study uses Microsoft's documentation as summarized in the Builder's
research notes dated October 26, 2026. Those notes were supplied offline; this
run did not fetch the linked pages or confirm their current contents. Package
metadata and XML API documentation were also inspected locally. No Visual
Studio instance was used. Documentation, source inspection, compilation,
installation and interactive behavior are different kinds of evidence here.

The requirement basis is master design revision `01M5V04238N81HC7BQQW7GH0XT`,
sections 3 and 12, HOST-01 through HOST-12, and section 13's ARC-03. Requirement
identifiers below refer to that private baseline; the descriptions are new
summaries. Decision `01M5TCAE8M4FN4AHRMPRNCW0ZA` postpones interactive Visual
Studio investigation until the Windows-machine decision. Section 3 includes
Visual Studio among the primary targets and excludes Visual Studio for Mac.
Neither inclusion nor this experiment qualifies a supported configuration.

## Options and their documented boundaries

| Option | Established mechanism | Boundary relevant to WeaveSharp |
| --- | --- | --- |
| Out-of-process VisualStudio.Extensibility | A .NET extension process; commands, document and editor APIs, tool windows, Project Query, output, settings, prompts, dialogs, language server providers and debugger visualizers [M1, M2]. | The feature list does not establish access to all debugger services or an existing Roslyn workspace. UI uses Remote UI rather than arbitrary extension-owned WPF controls [M3]. |
| VSSDK-compatible VisualStudio.Extensibility | Executes inside Visual Studio and can obtain SDK services through `MefInjection` and `AsyncServiceProviderInjection` [M4]. | This changes the process and runtime assumptions. For Visual Studio 2022 the documented target is .NET Framework 4.7.2, with `VssdkCompatibleExtension`, `RequiresInProcessHosting` and the manifest's `VSSDK+VisualStudio.Extensibility` extension type [M4]. |
| Classic Visual Studio SDK (VSSDK) | The existing SDK service surface, also exposed to the compatible model [M1, M4]. | A conventional in-process adapter is an alternative when a concrete SDK integration requires it. Service availability alone does not establish WeaveSharp's document, UI or debugger behavior. Individual service contracts need further investigation. |
| Language server provider | A documented extension point for language-server integration [M1]. | A possible contributor to analysis or language features, not a complete graph-host architecture. The supplied evidence does not establish reuse of Visual Studio's C# compilation context through this provider. |
| Debugger visualizer | A documented debugger extension point [M1]. | A possible focused display during debugging, not evidence of a general debugger adapter, persistent authoring surface, trace collector or Hot Reload controller. |

The last two rows are specialized extension points that can accompany a host
extension. Their columns below assess those mechanisms by themselves. They do
not imply that a containing extension cannot use other APIs.

Remote UI sends a XAML `DataTemplate` and proxied, serializable data to UI in
the Visual Studio process. The documented restrictions exclude custom
controls, event handlers and code-behind; referenced XAML types must already
exist in the Visual Studio process [M3]. A static label is consequently a useful
packaging probe. It provides no browser embedding, graph interaction or
accessibility evidence. Embedding WebView2 in a WPF tool window through the
in-process SDK route remains a proposed experiment, not an established result.

Microsoft recommends the compatible model for new extensions that require SDK
access [M4]. That makes it a reasonable bridge to investigate without moving
WeaveSharp's shared semantic service into Visual Studio's runtime. The minimal
extension in this change deliberately exercises the out-of-process template;
it does not implement the proposed in-process adapter.

## Needs against every option

**D — documented:** the cited material establishes the named primitive, not
the entire requirement's acceptance. **L — limited:** a documented facility
addresses only part of the need. **N — not available:** this mechanism alone
does not supply the requested responsibility, or its documented restrictions
exclude the proposed approach. **H — needs hands-on verification:** the evidence
does not settle the integration; inspect the relevant API contract first, then
test it on an approved Windows setup. H is not a claim that an API exists.

OOP, compatible and SDK refer to the first three options above. Their source
bases are [M1–M3], [M4], and the SDK comparison/access discussion in [M1, M4],
respectively. Provider and visualizer availability comes from [M1]. Cells that
refer to SDK access inherit that limited documentation basis; they do not
assert a successful Roslyn, WebView2 or debugger integration.

| WeaveSharp need and design anchor | OOP | Compatible | Classic SDK | Language server provider | Debugger visualizer |
| --- | --- | --- | --- | --- | --- |
| Open a host-owned view from a command (§12) | **D:** commands and Remote UI tool windows [M1–M3]. | **D:** extension commands plus SDK access [M4]; view behavior still untested. | **L:** SDK integration route [M4]; select and test the window implementation. | **N:** provider alone supplies no graph window. | **L:** a debugger-specific display, not a general authoring entry point. |
| Embed the shared browser graph and its accessible outline (§12; §13 rendering direction) | **N:** Remote UI cannot load an arbitrary custom browser control [M3]. No browser route established. | **H:** WebView2 in an SDK/WPF window; verify focus, keyboard, themes and lifetime. | **H:** same proposed browser experiment. | **N:** requires a separate UI integration. | **N:** no reusable authoring canvas established by this mechanism. |
| Read current text, including unsaved changes, and follow save/close (HOST-01) | **L:** editor/documents exist; buffer ownership and lifecycle need tests. | **H:** confirm SDK snapshots, dirty buffers and close behavior. | **H:** confirm the same document contract. | **L:** language integration exists; no complete host lifecycle established. | **N:** no general document synchronization contract. |
| Commit edits against the expected revision, with host undo (HOST-01) | **H:** prove stale-edit handling, undo grouping and coordinated edits. | **H:** prove host transactions and undo for graph edits. | **H:** same transaction proof required. | **L:** language integration does not establish the required transaction semantics. | **N:** not a document transaction mechanism. |
| Resolve linked files, active targets, references, symbols and compilation options (HOST-02; ARC-03) | **L:** Project Query is documented; equivalence to the live Roslyn compilation is unproven. | **H:** investigate `VisualStudioWorkspace`, including unsaved buffers and context switches. | **H:** same Roslyn investigation, including provider/version compatibility. | **L:** a server route is documented; agreement with host C# semantics is unproven. | **N:** an inspected value does not provide project-wide compilation context. |
| Carry host actions into the common graph/edit contracts (HOST-03) | **H:** implement the adapter and check shared contract behavior. | **H:** SDK access leaves adapter correctness to WeaveSharp. | **H:** same shared contract obligation. | **L:** may contribute analysis; cannot replace the full adapter. | **L:** may contribute a debug view; cannot replace the full adapter. |
| Navigate source and delegate project/build commands (§12) | **L:** command and document APIs exist; navigation and build delegation need API-specific proof. | **H:** establish supported SDK routes and ownership for each operation. | **H:** same command/navigation qualification. | **L:** language features cover only a subset of host operations. | **N:** no general project/build command route established. |
| Expose accurate capabilities for each configuration (HOST-04) | **H:** define and test capability reporting and unavailable operations. | **H:** same, including SDK service availability. | **H:** same, including version differences. | **L:** provider features are only one part of the capability record. | **L:** visualizer availability is only one part of the capability record. |
| Preserve selected session/frame, breakpoints, stepping ownership and multiple sessions (HOST-05) | **L:** visualizers are documented; full session/event/command access is unproven. | **H:** verify debugger services, events and paused-state access. | **H:** same debugger-service investigation. | **N:** language integration alone is not a debugger bridge. | **L:** debug display support does not establish session control or event coverage. |
| Keep normal editing usable after extension, analysis or trace failure (HOST-06) | **L:** a separate process helps isolation; recovery and source integrity require tests. | **H:** an in-process failure can affect the IDE; test service loss and recovery. | **H:** same in-process failure exposure. | **H:** test server loss with the containing adapter. | **H:** test visualizer failure without disrupting ordinary debugging. |
| Record editor builds, OS/CPU, project/runtime, providers, deployment and feature coverage (HOST-07; §3) | **L:** documented version/runtime baseline; no tested support row yet. | **L:** VS 2022 runtime requirements documented; actual combinations untested. | **H:** select and qualify exact SDK/IDE combinations. | **L:** provider presence is not a host support matrix. | **L:** visualizer presence is not a runtime/provider support matrix. |
| Use a permitted delivery route and review each dependency (HOST-08) | **L:** VSIX and package terms identified; redistribution review remains. | **L:** SDK/package terms and final VSIX contents require review. | **L:** review SDK dependencies and the actual distribution separately. | **H:** assess the chosen server's terms and deployment as well as its wrapper. | **H:** assess the chosen visualizer's dependencies and packaging. |
| Qualify viewing, editing, project features, debugging, tracing and Hot Reload separately (HOST-09) | **H:** six independent evidence sets; the template proves none of them. | **H:** qualify every category independently. | **H:** qualify every category independently. | **L:** language features cover only part of that list. | **L:** debug visualization covers only part of that list. |
| Place UI, analysis, builds and observation appropriately and assign transport ownership (HOST-10) | **L:** IDE/extension process separation is documented; remote-workspace placement is not. | **H:** keep UI in the IDE; verify supported access to workspace-side services. | **H:** same topology investigation. | **L:** a server boundary is useful, but remote deployment is unproven here. | **L:** debugger interaction does not establish the full remote topology. |
| Map workspace URIs and debug sources across machines and snapshots (HOST-11) | **H:** verify document identity and source mappings across each topology. | **H:** verify SDK identities and remote source correspondence. | **H:** same mapping requirement. | **H:** verify provider identities against host snapshots. | **H:** verify debug-source identity; no workspace-wide mapping established. |
| Handle cancellation, lost connections, restarts and late replies (HOST-12) | **H:** asynchronous APIs do not prove reconnection or freshness behavior. | **H:** test adapter and service lifetimes across disconnects. | **H:** same recovery tests. | **H:** test provider shutdown, resynchronization and obsolete replies. | **H:** invalidate old values and frames after resume/session changes. |
| Own analysis lifetime, isolate project state and release resources on close (ARC-03) | **L:** separate hosting is documented; workspace placement, isolation and restart remain unverified. | **H:** retain an independently managed semantic service; verify teardown and isolation. | **H:** same service boundary and lifecycle tests. | **L:** a server process can contribute; project isolation and ownership remain unverified. | **N:** a visualizer is not the analysis-service owner. |

The matrix covers API feasibility, not acceptance. Section 12's qualification
work must still exercise installation, opening projects, dirty buffers, undo,
context changes, session selection, source mapping, cancellation, lost
connections and unavailable dependencies. A successful installation or VSIX
build cannot replace that workflow evidence.

Section 3 also requires separate results for the project/runtime families a
release actually claims: ordinary SDK projects, file-based applications,
legacy .NET Framework, Unity/Mono, framework-specific desktop/web/mobile
projects, browser targets and AOT. None inherits debugger or trace coverage
from a working source editor. Visual Studio's local extension process should
not be confused with a service placed beside a remote workspace. Transport,
URI mapping, restart ownership and project-context fidelity remain open for
every proposed remote arrangement (HOST-10–12, ARC-03).

## Packaging, versions and dependency terms

The documented modern model starts with Visual Studio 2022 17.9 [M1, M2]. The
experiment targets `net8.0-windows8.0`, following the supplied sample baseline,
and references the two centrally pinned SDK/build packages at `17.14.40608`.
That does not make this particular extension compatible with 17.9: the cached
`ExtensionMetadata.InstallationTargetVersion` documentation derives its default
minimum from the referenced extensibility libraries [P1]. The generated
manifest must be inspected in Windows CI before reporting an installation
range. No runtime or architecture has been qualified interactively.

Microsoft describes open-ended installation targets for extensions built with
Visual Studio 2026, retaining Visual Studio 2022 coverage [M5]. This is a
packaging compatibility policy, not evidence that these APIs, dependencies or
WeaveSharp workflows work on every future IDE version.

The Visual Studio Marketplace is the intended distribution channel for an
eventual VSIX. Preparing an archive, obtaining Marketplace publication access
and qualifying its install/update behavior are separate steps. This experiment
is not published and does not request an account. Microsoft's Windows sample
CI uses full MSBuild for restore and build [M6]; the Builder owns the matching
CI work here. The Windows-only project stays outside `WeaveSharp.slnx`.

The offline lock contains 75 package entries. Local package metadata agrees
with the supplied inventory: 54 MIT entries and 21 using Microsoft files or
license URLs.
Both direct packages contain Microsoft's Visual Studio add-on/extension
terms, rather than an MIT grant [P2]. Those terms restrict use and distribution;
the repository's own license does not relicense them. The graph includes
`Microsoft.VisualStudio.Telemetry`; its presence is a dependency fact, not
evidence that the spike emits telemetry. Restore inputs, build-only tooling and
files ultimately included in a VSIX must be distinguished in the dependency
review. `PrivateAssets="all"` controls package propagation, not redistribution
rights or the generated VSIX's contents. The Builder regenerates the dependency
register and reviews the final archive before any release decision.

## Evidence from this change

The [experiment](../../experiments/visual-studio/README.md) contains one
contributed command, a tool window, a `RemoteUserControl`, an embedded XAML
template and string resources. The command requests the window through
`ShowToolWindowAsync`; the content is a static name label. No host document,
project, browser or debugger integration is implemented.

Offline restore generated the lock, and a second restore accepted it in locked
mode. The single local extension build attempt stopped at CS0246 because the
tool-window source lacked the `Microsoft.VisualStudio.RpcContracts.RemoteUI`
import for `IRemoteUserControl`. That import was corrected from the cached API
documentation. The attempt reported zero warnings and one error. The corrected
extension was not rebuilt locally, respecting the one-attempt limit. Intermediate
metadata from that attempt lists one command, one tool window and out-of-process
hosting; its installation targets are `[17.14,)` for amd64 and arm64. No VSIX,
successful extension compilation, installation or interactive UI result is
claimed. Windows full-MSBuild verification remains with the Builder. The
experiment README records the existing solution's separate check results.

## Sources

Microsoft links below identify the sources of the supplied October 26, 2026
research notes, not pages fetched during this sandbox run.

- **M1:** [VisualStudio.Extensibility overview](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility).
- **M2:** [First extension walkthrough](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/get-started/create-your-first-extension).
- **M3:** [Remote UI](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/inside-the-sdk/remote-ui).
- **M4:** [In-process extensions](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/get-started/in-proc-extensions).
- **M5:** [Visual Studio extension compatibility changes](https://devblogs.microsoft.com/visualstudio/modernizing-visual-studio-extension-compatibility-effortless-migration-for-extension-developers-and-users/).
- **M6:** [Microsoft samples' Windows workflow](https://github.com/microsoft/VSExtensibility/blob/main/.github/workflows/dotnet.yml). This is a moving upstream reference; no exact upstream commit was supplied or independently checked.
- **P1:** Locally cached `Microsoft.VisualStudio.Extensibility.Contracts` `17.14.2098`, `lib/netstandard2.0/Microsoft.VisualStudio.Extensibility.Contracts.xml`; SDK XML for `RemoteUserControl` and `ToolWindow`; `Microsoft.VisualStudio.RpcContracts` `17.14.20` XML for `IRemoteUserControl`. These establish API names and documented defaults, not runtime success.
- **P2:** [SDK package](https://www.nuget.org/packages/Microsoft.VisualStudio.Extensibility.Sdk/17.14.40608) and [build package](https://www.nuget.org/packages/Microsoft.VisualStudio.Extensibility.Build/17.14.40608), inspected through cached `.nuspec` and `LICENSE.txt` files. The exact resolved graph is in the experiment's committed `packages.lock.json`.
- **Requirements:** private master revision `01M5V04238N81HC7BQQW7GH0XT`, §§3, 12 and 13/ARC-03; deferral decision `01M5TCAE8M4FN4AHRMPRNCW0ZA`. Only identifiers and original summaries are included here.

## Recommendation for the host spike

Keep Visual Studio in the candidate set. First have Windows CI build this
minimal out-of-process project with full MSBuild and locked restore, inspect
the generated manifest/resources, and establish whether a VSIX is produced
without warnings. That resolves the build question only.

When interactive work is authorized, start the full host investigation with
the VSSDK-compatible model while keeping the common semantic engine separate.
Before committing to that route, verify these three dependencies together:

1. A WPF tool window can host the proposed WebView2 canvas with working focus,
   keyboard navigation, accessibility and disposal.
2. Supported Roslyn workspace access can combine dirty text with the correct
   compilation context, and supported document transactions preserve undo and
   refuse obsolete edits across project/target switches.
3. Supported debugger services expose the required events, selected sessions
   and frames, breakpoint state and paused values without taking over the
   user's debugging workflow.

Then qualify service restart, cancellation, project isolation and remote
source mapping, recording independent feature results for each claimed
configuration. If any of the three dependencies fails, keep the relevant host
capability unqualified and reassess the rendering or adapter choice. Use
classic VSSDK only when evidence identifies a reason to prefer it; use a
language server or debugger visualizer as a supplement where its narrower
contract helps. The present documentation and build probe justify further
investigation, not a host support claim.
