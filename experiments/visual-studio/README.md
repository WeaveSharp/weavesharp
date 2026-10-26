# Visual Studio build experiment

This experiment adds **Open WeaveSharp host spike** to the Extensions menu.
The command requests a floating tool window containing a Remote UI label,
**WeaveSharp host spike**. It is a small VisualStudio.Extensibility template
exercise for the [host study](../../docs/hosts/visual-studio.md), not an editor
adapter. It ships nothing.

`WeaveSharp.VisualStudio.Spike` contains an extension entry point, one command,
one tool window and a `RemoteUserControl`. `SpikeContent.xaml` is embedded under
the control's namespace and type name. Its `DataTemplate` uses a stock
`TextBlock`, with no data model, event handler or WPF code-behind. The window
owns and disposes the control. `.vsextension/string-resources.json` supplies the
extension metadata and command text; the SDK includes that directory in its
output. The generated manifest comes from the extension metadata.

The project follows the target used by the supplied Microsoft samples,
`net8.0-windows8.0`. `EnableWindowsTargeting` permits restore on other operating
systems. The repository still supplies C# 14, nullable analysis, warnings as
errors and lock-file generation. Both direct references omit versions and use
`PrivateAssets="all"`; `Directory.Packages.props` selects `17.14.40608`.
The committed `packages.lock.json` records the 75 resolved entries.

## Windows CI handoff

Windows CI is the build path for the VSIX. The Builder must add the separate
full-MSBuild step; this change does not add or run that step. Microsoft uses
full MSBuild in its [sample workflow](https://github.com/microsoft/VSExtensibility/blob/main/.github/workflows/dotnet.yml).
This Windows-specific project is intentionally absent from `WeaveSharp.slnx`,
whose normal build and tests continue across the existing CI matrix.

From a Windows environment with Visual Studio's MSBuild and the SDK selected
by the repository, the intended build commands are:

```powershell
msbuild experiments\visual-studio\WeaveSharp.VisualStudio.Spike\WeaveSharp.VisualStudio.Spike.csproj /t:Restore /p:RestoreLockedMode=true
msbuild experiments\visual-studio\WeaveSharp.VisualStudio.Spike\WeaveSharp.VisualStudio.Spike.csproj /t:Build /p:Configuration=Release /warnaserror
```

The Builder should require an unchanged lock file and a successful build with
no warnings, inspect the VSIX and its resources in the Release output, and
record the generated installation targets. The local generator emitted
`[17.14,)` for amd64 and arm64, but that intermediate file was produced during
a failed build. It is not an installation test or a packaged-artifact result.
The dependency register also needs the Builder's regeneration and review.

## Sandbox evidence, October 26, 2026

The sandbox was macOS 26.3 on arm64, using .NET SDK 10.0.403. All package inputs
were copied from the pre-existing local cache into an ignored workspace feed.
Package extraction, restore intermediates, build outputs, temporary files and
logs were directed into `.vericairn/scratch/`. No network operation was used.

| Check | Observed result |
| --- | --- |
| Generate the extension lock offline | Passed; 75 entries, with both direct packages at the central version. |
| Restore the extension with that lock | Passed with `--locked-mode` against the same feed. |
| One Release extension build, `--no-restore` | Failed: CS0246 for `IRemoteUserControl`; zero warnings, one error. |
| Correct that source error | Added the `Microsoft.VisualStudio.RpcContracts.RemoteUI` import, whose type is documented in the cached package. No second build was run under the brief's single-attempt limit. The final extension remains uncompiled locally. |
| Inspect generated metadata from the failed attempt | One command and one tool-window contribution, out-of-process hosting, and an intermediate VSIX manifest were present. No VSIX was produced. |
| Restore the existing solution in locked mode | Passed with one MSBuild node. An earlier restore produced no output and was interrupted; its cause was not established. |
| Build the existing solution in Release without restore | Passed: zero warnings and zero errors. |
| Execute the five existing test assemblies directly | Passed: 461 tests, no failures or skips. Counts: Coverage 34, Layout 72, Requirements 325, Smoke 1, Transformations 29. |
| Run `dotnet test` | Not run; the brief identifies a sandbox socket restriction. CI must exercise the test runner. |
| Linux, Windows and macOS CI | Not run or observed in this sandbox. Local macOS execution is not macOS CI evidence. |
| Windows full-MSBuild extension build and VSIX | Not verified. The corrected source and packaging must be checked there. |
| Installation, command execution, rendered UI and host behavior | Not exercised. Interactive Visual Studio work is deferred. |

Restores used a scratch `NuGet.Config`, scratch package paths and the required
command-line `NuGetAudit=false` override. No vulnerability-audit result is
claimed. Restore output included a `CSSM_ModuleLoad()` diagnostic; the initial
extension restore also reported a workload-verification issue. Both restores
completed successfully; those messages are retained in the local logs.

The local sequence used `dotnet restore --locked-mode`, followed by
`dotnet build --no-restore --configuration Release` and direct `dotnet`
execution of each test DLL. `--artifacts-path` redirected generated files, and
`-m:1`, `-nr:false` and `-p:UseSharedCompilation=false` kept the solution build
from depending on reusable compiler/build workers. The exact logs and the
final diff checks are recorded in the Vericairn work result.

These results establish the offline dependency graph and the unchanged
solution's local health. They do not establish successful compilation of the
corrected extension, VSIX production, rendering, browser hosting, live document
integration, project-context accuracy or debugger access. The study defines
the next host investigations after the Windows CI build.
