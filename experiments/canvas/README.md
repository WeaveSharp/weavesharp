# Keyboard graph experiment

This spike exercises graph authoring as semantic commands. A user journey can browse a palette, propose a node, inspect it, choose pins, request a connection, recover from a refusal, and disconnect using an explicit replacement. The tests drive these commands without pointer input. They simulate the host publishing the resulting graph; they do not apply C# edits.

`KeyboardSession.commit()` returns a protocol `prepare-edit` query serialized by SEC-04. It leaves the current graph untouched. Proposed items remain separately inspectable while a host preview is pending. A later publication supplies the authoritative nodes and relationships. The requested identity is a proposal: a real host must reconcile its result identity before offering this workflow. Cancellation here clears local proposal state; transport cancellation and source commit belong to the host integration.

The renderer produces two companion views: SVG geometry with an HTML label list, and an ARIA tree. Both carry the same semantic keys for nodes, pins, edges and regions. Pins and outgoing edges sit under their owning nodes in the tree. Regions list references to their members, which can overlap; protocol v0 has no parent-region field, so the renderer does not invent a region hierarchy. The node sequence follows the publication's order. Region expansion changes its member list, not the source graph.

Every element comes from SEC-04's DOM helpers. Labels use its bidirectional-text treatment, including visible warnings. Its label helper returns an HTML span, while its SVG vocabulary has no `foreignObject`. Consequently, labels live in valid HTML beside the SVG and geometry refers to them through `aria-labelledby`. They are not positioned inside the drawing. A host should load the supplied `src/canvas.css` as an existing packaged resource; the experiment does not create style elements or change host configuration.

| File | Purpose |
| --- | --- |
| `src/graph.ts` | Consume publications through `readInbound`, apply graph updates, describe identities and the sample pin catalogue. |
| `src/commands.ts` | Maintain focus, selection and proposals; produce source-preview requests through `writeOutbound`. |
| `src/layout.ts` | Assign logical coordinates and map points, boxes, pin sides and arrow commands. |
| `src/render.ts` | Build both views from the same publication and proposal state. |
| `test/` | Run journeys, inspect recorded DOM, check layout and exercise the existing sink scanner. |

The four `sample-*` palette entries are original demonstration operations. Protocol v0 supplies node kinds and types, but no named parameter signatures. Only those sample operations receive derived pins. Other kinds remain inspectable without guessed signatures. The candidate list checks the sample's direction, role, exact type, input occupancy and region rules. Actual C# binding, conversions, overloads, effects and lifetime checks remain a host responsibility. A candidate marked available is eligible for a preview request, not a declaration that a source transformation is legal.

A host can use `focusEntity()` with a view's `data-ws-key`, call `move()` using `arrowCommand()`, and bind the other semantic methods through its own command system. Pass `focus`, `selected`, `collapsed`, `proposals()` and `announcement` into `render()` after each change. The host must move real DOM focus, wire tree navigation and buttons, deliver announcements, and manage source previews. This package has no event adapter, browser entry page or host transport. It makes no reservation of host shortcuts.

From the repository root, with the already provided tools:

```sh
node node_modules/typescript/bin/tsc -p src/surface
node node_modules/typescript/bin/tsc -p experiments/canvas
node --test "src/surface/test/**/*.test.ts" "experiments/canvas/test/**/*.test.ts"
node experiments/canvas/test/benchmark.ts
```

Type checking emits no files. The root scripts include this experiment for existing CI. No package was added and no lock file was changed.

[EVIDENCE.md](EVIDENCE.md) records the measured sizes, test coverage and remaining host checks. This spike does not implement deletion, copying source, moving execution positions, runtime overlays, virtualization, source diffs or undo. Browser rendering, assistive technology and cross-platform CI were not exercised in the sandbox.
