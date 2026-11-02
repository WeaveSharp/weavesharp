# Host register

This page sets out, host by host, which C# tools and debugger WeaveSharp may lean on, how it reaches users, and which pieces it may ship inside its own extension. SEC-05 produced it on October 28, 2026, working from the licenses and marketplace terms listed under Sources and from the studies next to it ([VS Code](vs-code.md), [Visual Studio](visual-studio.md), [Rider](rider.md)). It was revised on November 2, 2026, after the Reviewer's review found that a Visual Studio extension ships Microsoft's SDK assemblies and that the runtime route and the publishers' obligations were missing, and again the same day after the Reviewer's second review refined the runtime routes. Each row reports the terms as written and the course WeaveSharp follows because of them; none of it is a lawyer's opinion, and the open points at the end wait for the operator.

WeaveSharp is Apache-2.0. Each extension is published by WeaveSharp under that license, with notices for anything it bundles.

## Rules for every host

- **Roslyn is WeaveSharp's own.** WeaveSharp bundles its own Microsoft.CodeAnalysis packages (MIT) and runs them in its own process. It never loads Roslyn binaries that ship inside another product, such as the C# extension's language server, which carry that product's license.
- **SDKs are for building, with one exception.** The VS Code API and the Rider SDK come from the host at run time, and WeaveSharp never ships them; the Rider SDK comes only through the Builder's Gradle build (decision D20). Visual Studio is different: Microsoft's own VisualStudio.Extensibility packaging puts SDK assemblies inside every extension's VSIX (see the Visual Studio row). Until the operator's legal reading settles question 4, no Visual Studio extension is released. The Visual Studio packages appear in `docs/dependencies/register.json` for experiments only, recorded but not restricted there because experiments ship nothing.
- **Microsoft's .NET debugger is driven only where its license allows.** vsdbg works only with Microsoft's Visual Studio Code, Visual Studio and Visual Studio for Mac. WeaveSharp never ships it, never launches it directly, and reaches it only through the host's own debugging.
- **Host tooling is optional.** WeaveSharp detects host tooling at run time and works without it where it can. It declares no hard dependency on an extension whose license limits who may use it.
- **WeaveSharp sends no telemetry and collects no personal data.** Every marketplace's privacy conditions below rest on this; a change to it is the operator's decision and changes this register first. A component WeaveSharp relies on that sends its own telemetry, such as the .NET Install Tool, is named in the listing with how to turn it off.
- **Anything moving from `experiments/` into a shipped extension passes this register first.** A license for code that ships is the operator's decision.

## Visual Studio Code (Microsoft's build)

| Concern | Recorded |
|---|---|
| C# tooling | The C# extension (ms-dotnettools.csharp): MIT source, but Microsoft's terms for the extension as published allow its use only with Visual Studio Code, Visual Studio or Xamarin Studio and forbid sharing it. C# Dev Kit: free for individuals, open source and academia; other organizations up to five users; larger enterprises need a Visual Studio subscription. Neither license addresses other extensions calling their commands; the C# extension's exports are undocumented. |
| May depend on | VS Code's API. The C# extension and Dev Kit only optionally, detected at run time: a hard dependency would pass Dev Kit's organization limits to WeaveSharp's users. The documented built-in commands (`vscode.execute*Provider`) rather than undocumented exports. |
| May bundle | WeaveSharp's own Roslyn and the runtime route chosen under "The .NET runtime" below. A permissively licensed debug adapter if a later work order chooses one. Not vsdbg, and not the C# extension's binaries. |
| Debugger route | The `coreclr` debug type through `vscode.debug.startDebugging`, owned by the C# extension (vsdbg), observed through debug adapter trackers. WS-09's observer records adapter traffic passively. |
| Marketplace | The Visual Studio Marketplace. Its offerings may be used only with Microsoft's in-scope products (Visual Studio, Visual Studio Code, GitHub Codespaces, Azure DevOps); WeaveSharp is the distributor and licenses the extension under Apache-2.0. Its obligations are listed under "Publisher obligations". |
| Evidence | WS-09's spike passes its integration tests in VS Code 1.142, and a Builder probe showed VS Code's webview enforcing the surface's policy and Trusted Types. |

## Compatible editors (VSCodium, Cursor, Windsurf, Antigravity)

| Concern | Recorded |
|---|---|
| C# tooling | Two texts keep Microsoft's C# tooling out of these editors. Dev Kit's terms name only Microsoft's Visual Studio Code, vscode.dev and Codespaces. For the C# extension, Microsoft's vsdbg page says the extension and its language services work with editors built from the open-source VS Code project, with the debugger alone limited to Microsoft's builds; what bars the extension is the Marketplace's rule that its offerings serve only Microsoft's products, and neither extension is published on Open VSX (checked November 2, 2026). Open VSX does carry community C# extensions, among them `muhammad-sammy.csharp` and `blipk.csharp` (the latter paired with netcoredbg). Cursor's own C# extension (`anysphere.csharp`) is not on Open VSX and has not been checked; nor has Antigravity's extension gallery, which is not documented officially. Both stay unverified until those editors are qualified. |
| May depend on | Each editor's VS Code API, verified per editor before WeaveSharp claims it; any C# extension only optionally. |
| May bundle | As for VS Code. |
| Debugger route | Not vsdbg. netcoredbg (MIT; release 3.2.0-1092 has Linux x64 and arm64, macOS arm64 and Windows x64 builds) or another permissively licensed adapter such as SharpDbg (MIT). Which one is a later decision with its own review. |
| Marketplace | Open VSX, under the Eclipse Foundation's Publisher Agreement 1.1: every offering carries a license, WeaveSharp is the distributor, and a namespace is claimed for verification. The same VSIX as the Visual Studio Marketplace. |
| Evidence | None hands-on yet; each editor is qualified for its tier from M2 on, never by assumption. |

## Visual Studio

| Concern | Recorded |
|---|---|
| C# tooling | Visual Studio's own Roslyn workspace and project system, through its extensibility APIs. |
| May depend on | Visual Studio's APIs. The SDK packages (Microsoft.VisualStudio.Extensibility.Sdk and Build 17.14.40608) come under Microsoft's terms for Visual Studio add-ons and extensions, which allow their use only with Visual Studio and VS Code, grant no right to distribute them, and forbid offering them to others on their own or inside another application. |
| What the VSIX holds | Microsoft.VisualStudio.Extensibility.Build packs the extension's whole output folder by default. For an extension that runs out of process it leaves out only the twelve assemblies the extension host provides (MessagePack, StreamJsonRpc, ServiceHub.Framework, RpcContracts, Telemetry and others), so Microsoft.VisualStudio.Extensibility, its Contracts and Framework assemblies, ProjectSystem.Query, Composition, CoreUtility, Utilities and RemoteControl ship inside it; WS-11's one-command VSIX was 3,828,075 bytes. The in-process model the Visual Studio study recommends leaves out none of those twelve and packs the Extensibility assemblies as well, since only components that ship with Visual Studio may leave them out; its VSIX therefore also holds Telemetry, ServiceHub.Framework and RpcContracts under the same Microsoft terms, and StreamJsonRpc, Newtonsoft.Json and MessagePack under MIT. Several of these packages carry a license file or URL rather than a license on the allowed list in `docs/dependencies/license-policy.json`, so shipping them would also need an exception there. |
| May bundle | WeaveSharp's own Roslyn, in its own process. The SDK assemblies Microsoft's packaging adds only if the operator's legal reading (question 4) finds their terms allow it, with an exception in the license policy. |
| Debugger route | Visual Studio's debugger through its documented SDK services (`IVsDebugger4` in the in-process model). Whether VisualStudio.Extensibility offers an out-of-process route is open (WS-11). |
| Marketplace | The Visual Studio Marketplace, under the same terms and publisher agreement as VS Code's. Visual Studio Community 2026 lets any organization use it for open-source applications, Visual Studio extensions, Windows drivers, SQL Server Data Tools projects, and classroom or academic work; outside those uses, an organization that is not an enterprise may have up to five concurrent users, and an enterprise (more than 250 PCs or users, or more than one million US dollars in annual revenue) may not use it. Building WeaveSharp's extension falls under the extensions use. |
| Evidence | WS-11's study and the minimal extension Windows CI builds into a VSIX; hands-on work waits for a Windows machine. |

## Rider

| Concern | Recorded |
|---|---|
| C# tooling | ReSharper's code model on Rider's backend, reached through a backend plugin and the rd protocol (M2). |
| May depend on | Rider's documented platform APIs. The Rider SDK (license 1.2) may be used solely to design, develop and test plug-ins, and may not be redistributed. The Marketplace's Approval Guidelines forbid internal APIs. |
| May bundle | WeaveSharp's own Roslyn, in its own process, with the runtime route chosen under "The .NET runtime" below. Not the Rider SDK. |
| Debugger route | The platform's XDebugger APIs, which WS-10's trial observes without driving. Starting a .NET session the way JetBrains' plugins do relies on `com.jetbrains.rider.run.IDotNetDebugProfileState`, an undocumented class; the Plugin Verifier, or JetBrains if necessary, decides whether WeaveSharp may call it. |
| Marketplace | The JetBrains Marketplace, under Developer Agreement 3.2. WeaveSharp supplies a Developer EULA built from its Apache-2.0 terms, links the source code and signs each upload, which the Marketplace counter-signs. Users need their own Rider license. The free non-commercial tier excludes commercial work, and under its agreement (version 3.0) Rider collects usage data by default: only the detailed code-related part can be switched off, and JetBrains may also check compliance with its terms by having Rider send data about the frameworks, templates and actions used, with no switch. |
| Build inputs | Gradle drives the build and fetches from Maven Central, the Gradle Plugin Portal and JetBrains' own repositories, roughly 4 GB per Rider version. For now that happens only on the Builder's Mac with every artifact pinned by SHA-256 (decision D20); the CI workflows still use nuget.org and npmjs.org alone. |
| Evidence | WS-10's trial, built and run by the Builder in Rider 2026.2.3: the surface embeds from the plugin's own resource handler, and a native edit with Rider's undo works. Rider runs JCEF out of process, with a helper listening on the loopback interface. |

## The .NET runtime for WeaveSharp's own process

WeaveSharp's Roslyn runs in a process of its own, and that process needs a .NET runtime. The runtime is MIT, and so is Microsoft's .NET Install Tool; both bring third-party notices with them. Each host offers different routes, and the choice for each is made when its adapter is planned, with its notices added to the dependency register and the extension's notices file. VS Code's comes first, because 0.1.0 is planned for VS Code.

| Host | Routes | Course |
|---|---|---|
| VS Code and compatible editors | A self-contained build in a per-platform VSIX, which ships the runtime itself and sends nothing. Or a framework-dependent build that obtains a runtime through the .NET Install Tool (`ms-dotnettools.vscode-dotnet-runtime`, MIT, published on both the Visual Studio Marketplace and Open VSX), as Microsoft's C# extension and the community C# extensions do; that tool sends Microsoft telemetry by default, including which extension asked for a runtime, until the user turns off its `dotnetAcquisitionExtension.enableTelemetry` setting. | Chosen in the VS Code adapter's planning; either way the extension states which runtime it uses and where it came from, and the framework-dependent route names the tool's telemetry in the listing. |
| Visual Studio | It depends on the extension model. An out-of-process extension runs in Visual Studio's extension host on a .NET runtime Visual Studio provides, which WeaveSharp's analysis could use only by running inside that host. An in-process extension runs inside Visual Studio itself on .NET Framework, so a separate analysis process gets no runtime from the host. That process can be self-contained, rely on a shared .NET runtime that some Visual Studio workloads install (MIT, but not guaranteed to be present), or reuse Visual Studio's private runtime copies, which is a question of Visual Studio's terms. | Open: chosen in the Visual Studio adapter's planning with the extension model; self-contained is the route that needs no further reading. |
| Rider | The runtime Rider bundles serves ReSharper's backend. Whether another product may start its own process on it is a question of JetBrains' terms; otherwise the self-contained or installed routes above apply. | Chosen in the Rider adapter's planning, after asking JetBrains if reuse is wanted. |

## Publisher obligations

Each marketplace puts duties on WeaveSharp as the publisher. This is how each is met; the release checklist for each host repeats the list.

| Marketplace | Obligation | How WeaveSharp meets it |
|---|---|---|
| Visual Studio Marketplace | WeaveSharp distributes its extension, with Microsoft acting as its agent (Publisher Agreement 3(b)). | The listing names WeaveSharp as publisher and Apache-2.0 as the license. |
| Visual Studio Marketplace | A privacy policy is required if the extension accesses, collects or transmits personal data (8(b)(ii)); the Terms of Use ask that an offering's terms be easy to find. | WeaveSharp itself collects and sends nothing; the listing says so, links the license, and names any relied-on component that sends its own telemetry. |
| Visual Studio Marketplace | The publisher keeps to trade and export laws (13) and to the licenses of the third-party and open-source code it includes (4). | Each shipped package is in the dependency register; the extension carries its notices; WeaveSharp ships nothing export-controlled. |
| Open VSX | Every offering carries a license, and the publisher may distribute what it uploads (Publisher Agreement 1.1, section 4(a)); a listing discloses any data the extension collects (section 8). | Apache-2.0, with notices for what is bundled; the same VSIX as the Visual Studio Marketplace; the listing says WeaveSharp collects nothing and names any relied-on component's telemetry. |
| JetBrains Marketplace | The plugin must be authorized for export and its developer free of export restrictions (Developer Agreement 3.2, 20.1(f) and (h)); third-party open source needs its attributions and notices, and its source where its terms require (20.1(g)). | WeaveSharp ships nothing export-controlled; notices travel with the plugin, and WeaveSharp's own source is public. |
| JetBrains Marketplace | A Developer EULA names WeaveSharp as licensor, with JetBrains no party to it (10.2). | The EULA is built from WeaveSharp's Apache-2.0 terms. |
| JetBrains Marketplace | Telemetry needs the user's explicit permission and may not be sent while the plugin is idle; personal data needs a privacy policy (Approval Guidelines 1.3, 2.2(d) and (e), 3.4). | WeaveSharp sends no telemetry and collects no personal data; the listing says so. |
| Every marketplace | MIT and Apache-2.0 notices for bundled code, among them Roslyn's; the origin and license of every listing asset, such as icons. | Notices files in each extension; listing assets recorded in the dependency register when they are made. |

## Questions for the operator

1. **The Rider SDK license** allows using the SDK to design, develop and test plug-ins but never expressly grants distributing them, though the Marketplace agreement assumes developers do. The license defines the SDK to include associated Rider software files, so the reading should also say whether that reaches the Rider distribution WeaveSharp's Gradle build fetches and compiles against (riderRD 2026.2.3). A legal reading is needed before WeaveSharp's first Rider release.
2. **The C# extension's exports** (`experimental.sendServerRequest`, `getComponentFolder`) are undocumented. WeaveSharp does not use them; asking Microsoft whether they may be used is optional.
3. **Rider's debugger profile API** is undocumented. The Plugin Verifier, and if needed JetBrains, settle whether WeaveSharp may use it.
4. **The Visual Studio SDK assemblies in the VSIX.** Microsoft's own build tooling puts SDK assemblies inside every VisualStudio.Extensibility extension, while the terms on those packages grant no distribution right. A legal reading is needed before WeaveSharp's first Visual Studio release: whether those terms permit what the tooling packs, for the extension model WeaveSharp chooses (an in-process VSIX also holds the host-shared assemblies, Telemetry, ServiceHub.Framework and RpcContracts among them), and, if they do, an exception in the license policy for the packages whose licenses are not on its allowed list.

## Sources

Read on October 26 and 27, 2026, and again on November 2, 2026 for the revision.

- Visual Studio Code license: https://code.visualstudio.com/license
- Visual Studio Marketplace Terms of Use: https://aka.ms/vsmarketplace-ToU
- Visual Studio Marketplace Publisher Agreement: https://aka.ms/vsmarketplace-agreement
- Open VSX Publisher Agreement: https://www.eclipse.org/legal/documents/eclipse-openvsx-publisher-agreement.pdf
- C# extension runtime license: https://github.com/dotnet/vscode-csharp/blob/main/RuntimeLicenses/license.txt
- C# Dev Kit license: https://marketplace.visualstudio.com/items/ms-dotnettools.csdevkit/license
- vsdbg licensing: https://github.com/dotnet/vscode-csharp/blob/main/docs/debugger/Microsoft-.NET-Core-Debugger-licensing-and-Microsoft-Visual-Studio-Code.md
- Open VSX listings: https://open-vsx.org/extension/muhammad-sammy/csharp and https://open-vsx.org/extension/blipk/csharp
- .NET Install Tool, including its telemetry notice: https://github.com/dotnet/vscode-dotnet-runtime and https://open-vsx.org/extension/ms-dotnettools/vscode-dotnet-runtime
- Visual Studio in-process extensions: https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/get-started/in-proc-extensions
- .NET runtime license: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- netcoredbg releases: https://github.com/Samsung/netcoredbg/releases
- SharpDbg: https://github.com/MattParkerDev/sharpdbg
- Visual Studio Extensibility SDK license: https://www.nuget.org/packages/Microsoft.VisualStudio.Extensibility.Sdk/17.14.40608/license
- Visual Studio Extensibility build package (its targets decide what the VSIX holds): https://www.nuget.org/packages/Microsoft.VisualStudio.Extensibility.Build/17.14.40608
- Visual Studio Community 2026 license: https://visualstudio.microsoft.com/license-terms/vs2026-ga-community/
- Rider SDK license: https://www.jetbrains.com/legal/docs/rider/sdk_license/
- JetBrains Marketplace Developer Agreement: https://www.jetbrains.com/legal/docs/plugins_site/developer-agreement/
- JetBrains Marketplace Approval Guidelines: https://plugins.jetbrains.com/docs/marketplace/jetbrains-marketplace-approval-guidelines.html
- Rider non-commercial license: https://www.jetbrains.com/legal/docs/toolbox/license_non-commercial/
- Roslyn license: https://github.com/dotnet/roslyn/blob/main/License.txt
