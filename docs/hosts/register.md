# Host register

This page sets out, host by host, which C# tools and debugger WeaveSharp may lean on, how it reaches users, and which pieces it may ship inside its own extension. SEC-05 produced it on October 28, 2026, working from the licenses and marketplace terms listed under Sources and from the studies next to it ([VS Code](vs-code.md), [Visual Studio](visual-studio.md), [Rider](rider.md)). Each row reports the terms as written and the course WeaveSharp follows because of them; none of it is a lawyer's opinion, and the open points at the end wait for the operator.

WeaveSharp is Apache-2.0. Each extension is published by WeaveSharp under that license, with notices for anything it bundles.

## Rules for every host

- **Roslyn is WeaveSharp's own.** WeaveSharp bundles its own Microsoft.CodeAnalysis packages (MIT) and runs them in its own process. It never loads Roslyn binaries that ship inside another product, such as the C# extension's language server, which carry that product's license.
- **No host's SDK is redistributed.** The Visual Studio SDK packages and the Rider SDK are used to build and test; the host supplies them at run time. The Visual Studio packages appear in `docs/dependencies/register.json` for experiments only, recorded but not restricted there because experiments ship nothing; the Rider SDK comes only through the Builder's Gradle build (decision D20).
- **Microsoft's .NET debugger is driven only where its license allows.** vsdbg works only with Microsoft's Visual Studio Code, Visual Studio and Visual Studio for Mac. WeaveSharp never ships it, never launches it directly, and reaches it only through the host's own debugging.
- **Host tooling is optional.** WeaveSharp detects host tooling at run time and works without it where it can. It declares no hard dependency on an extension whose license limits who may use it.
- **Anything moving from `experiments/` into a shipped extension passes this register first.** A license for code that ships is the operator's decision.

## Visual Studio Code (Microsoft's build)

| Concern | Recorded |
|---|---|
| C# tooling | The C# extension (ms-dotnettools.csharp): MIT source, but its runtime binaries may be used only with Visual Studio Code, Visual Studio or Xamarin Studio and may not be shared. C# Dev Kit: free for individuals, open source and academia; other organizations up to five users; larger enterprises need a Visual Studio subscription. Neither license addresses other extensions calling their commands; the C# extension's exports are undocumented. |
| May depend on | VS Code's API. The C# extension and Dev Kit only optionally, detected at run time: a hard dependency would pass Dev Kit's organization limits to WeaveSharp's users. The documented built-in commands (`vscode.execute*Provider`) rather than undocumented exports. |
| May bundle | WeaveSharp's own Roslyn. A permissively licensed debug adapter if a later work order chooses one. Not vsdbg, and not the C# extension's binaries. |
| Debugger route | The `coreclr` debug type through `vscode.debug.startDebugging`, owned by the C# extension (vsdbg), observed through debug adapter trackers. WS-09's observer records adapter traffic passively. |
| Marketplace | The Visual Studio Marketplace. Its offerings may be used only with Microsoft's in-scope products (Visual Studio, Visual Studio Code, GitHub Codespaces, Azure DevOps); WeaveSharp is the distributor and licenses the extension under Apache-2.0. |
| Evidence | WS-09's spike passes its integration tests in VS Code 1.142, and a Builder probe showed VS Code's webview enforcing the surface's policy and Trusted Types. |

## Compatible editors (VSCodium, Cursor, Windsurf, Antigravity)

| Concern | Recorded |
|---|---|
| C# tooling | Microsoft's C# extension and Dev Kit are not licensed for these editors and are not on Open VSX. Cursor ships its own C# extension (anysphere.csharp); Open VSX carries community C# extensions. |
| May depend on | Each editor's VS Code API, verified per editor before WeaveSharp claims it; any C# extension only optionally. |
| May bundle | As for VS Code. |
| Debugger route | Not vsdbg. netcoredbg (MIT; release 3.2.0-1092 has Linux x64 and arm64, macOS arm64 and Windows x64 builds) or another permissively licensed adapter such as SharpDbg (MIT). Which one is a later decision with its own review. |
| Marketplace | Open VSX, under the Eclipse Foundation's Publisher Agreement 1.1: every offering carries a license, WeaveSharp is the distributor, and a namespace is claimed for verification. The same VSIX as the Visual Studio Marketplace. Antigravity's gallery is not documented officially and must be verified before WeaveSharp claims it. |
| Evidence | None hands-on yet; each editor is qualified for its tier from M2 on, never by assumption. |

## Visual Studio

| Concern | Recorded |
|---|---|
| C# tooling | Visual Studio's own Roslyn workspace and project system, through its extensibility APIs. |
| May depend on | Visual Studio's APIs. The SDK packages (Microsoft.VisualStudio.Extensibility.Sdk and Build 17.14.40608, under Microsoft's add-ons and extensions terms) are used only to build; they may not be shared or combined for others to use. |
| May bundle | WeaveSharp's own Roslyn, out of process. Not the SDK's binaries. |
| Debugger route | Visual Studio's debugger through its documented SDK services (`IVsDebugger4` in the in-process model). Whether VisualStudio.Extensibility offers an out-of-process route is open (WS-11). |
| Marketplace | The Visual Studio Marketplace, under the same terms and publisher agreement as VS Code's. Visual Studio Community allows organizations unlimited use for open-source projects and Visual Studio extensions, otherwise five users. |
| Evidence | WS-11's study and the minimal extension Windows CI builds into a VSIX; hands-on work waits for a Windows machine. |

## Rider

| Concern | Recorded |
|---|---|
| C# tooling | ReSharper's code model on Rider's backend, reached through a backend plugin and the rd protocol (M2). |
| May depend on | Rider's documented platform APIs. The Rider SDK (license 1.2) may be used solely to design, develop and test plug-ins, and may not be redistributed. The Marketplace's Approval Guidelines forbid internal APIs. |
| May bundle | WeaveSharp's own Roslyn, in its own process. Not the Rider SDK. |
| Debugger route | The platform's XDebugger APIs, which WS-10's trial observes without driving. Starting a .NET session the way JetBrains' plugins do relies on `com.jetbrains.rider.run.IDotNetDebugProfileState`, an undocumented class; the Plugin Verifier, or JetBrains if necessary, decides whether WeaveSharp may call it. |
| Marketplace | The JetBrains Marketplace, under Developer Agreement 3.2. WeaveSharp supplies a Developer EULA built from its Apache-2.0 terms, links the source code and signs each upload, which the Marketplace counter-signs. Users need their own Rider license: the free tier excludes commercial work and shares usage data unless switched off. |
| Build inputs | Gradle drives the build and fetches from Maven Central, the Gradle Plugin Portal and JetBrains' own repositories, roughly 4 GB per Rider version. For now that happens only on the Builder's Mac with every artifact pinned by SHA-256 (decision D20); the CI workflows still use nuget.org and npmjs.org alone. |
| Evidence | WS-10's trial, built and run by the Builder in Rider 2026.2.3: the surface embeds from the plugin's own resource handler, and a native edit with Rider's undo works. Rider runs JCEF out of process, with a helper listening on the loopback interface. |

## Questions for the operator

1. **The Rider SDK license** allows using the SDK to design, develop and test plug-ins but never expressly grants distributing them, though the Marketplace agreement assumes developers do. A legal reading is needed before WeaveSharp's first Rider release.
2. **The C# extension's exports** (`experimental.sendServerRequest`, `getComponentFolder`) are undocumented. WeaveSharp does not use them; asking Microsoft whether they may be used is optional.
3. **Rider's debugger profile API** is undocumented. The Plugin Verifier, and if needed JetBrains, settle whether WeaveSharp may use it.

## Sources

Read on October 26 and 27, 2026.

- Visual Studio Code license: https://code.visualstudio.com/license
- Visual Studio Marketplace Terms of Use: https://aka.ms/vsmarketplace-ToU
- Visual Studio Marketplace Publisher Agreement: https://aka.ms/vsmarketplace-agreement
- Open VSX Publisher Agreement: https://www.eclipse.org/legal/documents/eclipse-openvsx-publisher-agreement.pdf
- C# extension runtime license: https://github.com/dotnet/vscode-csharp/blob/main/RuntimeLicenses/license.txt
- C# Dev Kit license: https://marketplace.visualstudio.com/items/ms-dotnettools.csdevkit/license
- vsdbg licensing: https://github.com/dotnet/vscode-csharp/blob/main/docs/debugger/Microsoft-.NET-Core-Debugger-licensing-and-Microsoft-Visual-Studio-Code.md
- netcoredbg releases: https://github.com/Samsung/netcoredbg/releases
- SharpDbg: https://github.com/MattParkerDev/sharpdbg
- Visual Studio Extensibility SDK license: https://www.nuget.org/packages/Microsoft.VisualStudio.Extensibility.Sdk/17.14.40608/license
- Visual Studio Community 2026 license: https://visualstudio.microsoft.com/license-terms/vs2026-ga-community/
- Rider SDK license: https://www.jetbrains.com/legal/docs/rider/sdk_license/
- JetBrains Marketplace Developer Agreement: https://www.jetbrains.com/legal/docs/plugins_site/developer-agreement/
- JetBrains Marketplace Approval Guidelines: https://plugins.jetbrains.com/docs/marketplace/jetbrains-marketplace-approval-guidelines.html
- Rider non-commercial license: https://www.jetbrains.com/legal/docs/toolbox/license_non-commercial/
- Roslyn license: https://github.com/dotnet/roslyn/blob/main/License.txt
