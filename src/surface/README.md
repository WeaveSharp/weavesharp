# Graph surface

The graph surface is WeaveSharp's TypeScript user interface, shown inside each editor's webview: VS Code's, Rider's and Visual Studio's are all Chromium-based. Everything it shows comes from source code, names, comments, layout files and protocol messages, so all of it is untrusted data. `src/isolation/` is the layer everything else builds on; the canvas comes later (WS-08).

## The isolation layer

- **Content-Security-Policy (`csp.ts`).** Nothing loads or runs unless it is the surface's own script or style carrying the page's nonce, or a file from a declared HTTPS resource origin. There are no network connections, frames, workers, plugins, forms or base URL changes. Trusted Types are required with no policy, so every HTML-string and script-string sink throws at run time.
- **The page (`page.ts`).** Every host serves the surface in the same fixed HTML shell from `surfacePage`. The encoding and policy come first, and the page load's nonce is on each module script and stylesheet. Each resource must be an HTTPS URL under a declared resource origin, matched as the browser matches a CSP host source. Hosts pass values, never markup, and every value is escaped where it lands.
- **DOM builder (`dom.ts`).** The only way the surface creates DOM. It creates allow-listed elements only, sets allow-listed attributes with checked values, and puts all text in text nodes. Event handlers, styles, URLs and frames can't be expressed at all.
- **Labels (`text.ts`, `dom.ts`).** Untrusted text is bounded and shown in a span that isolates its own direction. Bidirectional controls (embeddings, overrides, isolates and marks), control characters and invisible characters (default-ignorable and format characters, and line and paragraph separators, but not the joiners and variation selectors that shape scripts and emoji) become visible markers, with a warning badge that names them for assistive technology: the defense editors use against the Trojan Source attack (CVE-2021-42574). Tooltips and anything assistive technology may announce (`title` and every `aria-*` attribute that is not a list of element references) get the same markers; reference lists hold identifiers only.
- **Messages (`messages.ts`, `schema.ts`).** Messages from the host arrive as JSON text. They are refused before parsing when they're too long or nest too deep, refused when they carry `__proto__`, `constructor` or `prototype` keys, limited to the protocol families and kinds the surface may receive, and validated against protocol v0's schemas (`protocol/v0/`). Outgoing commands are limited to an allow-list and validated the same way. Refusals name the rule, never the content, and a reader refuses rather than throws, even when validation runs out of its work budget. Host adapters use the other side of the same boundary: `readFromSurface` reads what a webview sends with the same bounds, key check and schemas, limited to the surface's command allow-list, and `writeToSurface` serializes only what the surface accepts.
- **Sink scan (`sinks.ts`).** A test scans the surface's source, across line breaks, and fails on any sink that parses HTML, runs a string as code, navigates or writes styles, including through computed members, reflection and timers given anything but a function. Only the DOM builder, by its repository path, may set attributes or create elements by name. The scan is a backstop; the page's policy and Trusted Types refuse these sinks at run time.

## Running it

From the repository root, with Node.js 24 or later:

```sh
npm ci --ignore-scripts
npm run typecheck
npm test
```

Tests run TypeScript directly on Node.js's type stripping. TypeScript itself only type-checks; nothing is emitted.
