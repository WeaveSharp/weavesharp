# Rider graph trial

This frontend plugin adds a **WeaveSharp trial** tab beside Rider's C# editor.
It displays the shared canvas and outline from the current document buffer.
The projection recognizes a deliberately small syntax sample. It supplies no
C# binding, ReSharper backend, rd model, or shared analysis service.

The Rider TypeScript build and ten Node tests passed offline with the supplied
TypeScript 7.0.2 and Node 26.6.0. Kotlin and Gradle were reviewed as source;
neither was executed. The full local Node check set passed 200 tests, and
all five TypeScript configurations passed. Browser embedding, native undo and debugger events
therefore remain unverified in an IDE. See the [API study](../../docs/hosts/rider.md)
for capability labels, dependency sources and the Builder's remaining work.

## Code and generated resources

| Location | Responsibility |
| --- | --- |
| `webview/page.ts`, `webview/generate.ts` | Generate the SEC-04 page, matching response policy and fixed resource inventory. |
| `webview/receive.ts`, `webview/client.ts`, `webview/entry.ts` | Decode host deliveries, call SEC-04, display both views and handle keyboard focus. |
| `src/main/kotlin/com/weavesharp/rider/SurfaceResources.kt` | Resolve exact packaged URLs, substitute the nonce and encode host messages. |
| `src/main/kotlin/com/weavesharp/rider/SurfaceBrowser.kt` | Attach JCEF request and UI handlers. |
| `src/main/kotlin/com/weavesharp/rider/GraphEditor.kt` | Register the extra editor, follow document changes and publish snapshots. |
| `src/main/kotlin/com/weavesharp/rider/InsertStatementAction.kt` | Perform the sample insertion in a native write command. |
| `src/main/kotlin/com/weavesharp/rider/StandIn.kt` | Project the small syntax grammar into protocol v0. |
| `src/main/kotlin/com/weavesharp/rider/DebugObserver.kt` | Observe sessions while the project service is alive. |
| `test/`, `src/test/kotlin/` | Executable TypeScript checks and deferred JUnit checks. |

One TypeScript configuration emits ES modules into `out/`. The generator
copies the browser's module closure and the existing canvas CSS into
`out/resources/surface`. The shared schema module comes from WS-09; a test
compares it with `protocol/v0`. Tests and the generator are absent from the
served inventory. Gradle's resource task depends on both generation steps.
Generated output is ignored by Git.

The browser opens `https://surface.weavesharp.invalid/index.html`. An exact
URL lookup serves packaged bytes; the reserved hostname needs no resolver or
server. Unknown URLs, methods and download requests receive a refusal with
default resource handling disabled. Only the initial host navigation is
admitted. Popup and context-menu handlers suppress those interfaces, and no
DevTools UI is provided.

SEC-04 generates the entire page. Its placeholder has 22 Base64 payload
characters followed by `==`, for 24 characters total. Each page response
uses 16 new `SecureRandom` bytes and replaces that placeholder in both the
page and its generated policy. The header adds `frame-ancestors 'none'` to
SEC-04's policy. Other resource bytes are unchanged.

JCEF's load event starts delivery after the module has loaded. The host
executes a fixed receiver call containing standard Base64. The receiver uses
`atob`, fatal UTF-8 decoding and `readInbound`. The page has no path to call
the plugin. Its disabled Apply control explains how to use the native action.
Both ends assume protocol 0.0; this is not version negotiation.

## Offline TypeScript checks

From the repository root, using the already installed tools:

```sh
node node_modules/typescript/bin/tsc -p experiments/rider --noEmit
node node_modules/typescript/bin/tsc -p experiments/rider
node experiments/rider/out/experiments/rider/webview/generate.js
node --test "experiments/rider/out/experiments/rider/test/*.test.js"
```

The root `typecheck` and `test` scripts include these steps for the existing
CI workflow. No npm dependency or lock file was changed. This run did not
restore packages or invoke CI. The tests cover the generated page and header,
resource closure, schema freshness, decoding failures, SEC-04 refusals,
publication refresh, disabled commits and the SEC-04 sink scan.

## Builder run, only after operator approval

The sandbox must not execute Gradle or Kotlin. An approved Builder run needs
Gradle 9 or later, Java 21, Node and the repository's locked npm installation.
The version catalogue pins the IntelliJ Platform plugin to 2.19.0 and Rider
to 2026.2.3. **Kotlin 2.3.20 is a candidate pairing**, not an established match
for platform 262. Confirm that pairing and the bundled JCEF dependency before
building. No Gradle wrapper or downloaded artifact is supplied.

The Builder creates the wrapper with a pinned distribution checksum, prepares
the dependency caches and performs the dependency review described in the
study. `test` runs the JUnit checks; `buildPlugin` creates the plugin archive;
`runIde` launches the experimental IDE. Use Gradle's offline option once those
caches are complete. These task names document a future approved run; none
was invoked here. Do not infer installation success from the TypeScript tests.

For the document journey, use a new scratch project with this original sample:

```csharp
class Meter
{
    static void Tick()
    {
        int count = 4;
        count++;
        return;
    }
}
```

Open its graph tab, return to the text editor, put the caret inside `count++`,
and invoke **Insert sample statement** from the editor menu or Find Action.
The action inserts `; ` before that statement under `WriteCommandAction`.
Verify that the graph refreshes, one native undo restores the previous text,
and redo reapplies it. Ordinary edits also trigger publication. Before the
page is ready, changes are represented by the snapshot sent when loading
finishes. Closing the editor disposes its browser and document listener.

The grammar accepts one class, one parameterless void method, integer literal
declarations/assignments, increments/decrements, empty statements and bare
returns. Initial line comments are allowed. Unsupported statements break
sequence edges and disable the action; other method shapes are unavailable.
The sample budget is 512 statements and 65,536 UTF-16 units. This limit keeps
the demonstration small. Graph revisions advance independently of document
stamps so an undo can restore an older stamp without losing the refresh.

Opening a graph starts the project's debugger observer. It distinguishes
session identities and keeps at most 256 observations, including pauses,
resume, selected-position changes and stops. It records a source URL and
zero-based line when available. Already-running sessions are marked attached.
It also writes these observations through Rider's logger; no separate file
store is implemented. It requests no evaluations and captures no values.
IDE logging may retain the source location. Graph overlays and precise
expression mapping are outside this trial.
