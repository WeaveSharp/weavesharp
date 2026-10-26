import assert from "node:assert/strict";
import { test } from "node:test";
import { readFromSurface, readInbound, writeToSurface } from "../../../src/surface/src/isolation/index.ts";
import { GraphEditor } from "../src/editor.ts";
import { SurfaceClient } from "../src/surface-client.ts";
import { schemas } from "../src/schemas.generated.ts";
import { envelope } from "../src/wire.ts";
import { MemoryDocument, payload, sample } from "./support.ts";
import type { Graph } from "../../canvas/src/graph.ts";
import type { Edit } from "../src/wire.ts";

function connected() {
  const outgoing: string[] = [];
  const document = new MemoryDocument();
  const client = new SurfaceClient("page-1", (json) => outgoing.push(json));
  const editor = new GraphEditor(document, "view", async (json) => client.read(json));
  document.onChange = () => editor.changed();
  async function flush() {
    while (outgoing.length > 0) {
      const json = outgoing.shift()!;
      assert.equal(readFromSurface(json, schemas).accepted, true);
      await editor.handle(json);
    }
  }
  return { client, editor, document, outgoing, flush };
}
test("a fresh page obtains its own graph, previews, waits for confirmation and applies once", async () => {
  const { client, document, outgoing, flush } = connected();
  client.start();
  await flush();
  assert.equal(client.session?.graph.nodes.length, 3);
  client.prepare();
  await flush();
  assert.notEqual(client.proposal, undefined);
  assert.equal(document.text, sample);
  assert.equal(outgoing.length, 0);
  client.confirm();
  client.confirm();
  assert.equal(outgoing.length, 1);
  await flush();
  assert.equal(document.applied.length, 1);
  assert.equal(client.session?.graph.nodes.length, 4);
  assert.equal(client.proposal, undefined);
  await document.undo();
  assert.equal(client.session?.graph.nodes.length, 3);
  await document.redo();
  assert.equal(client.session?.graph.context.snapshot.hostVersion, "4");
});
test("cancel dismisses a preview and a later source update retires it", async () => {
  const { client, document, outgoing, flush } = connected();
  client.start(); await flush();
  client.prepare(); await flush();
  client.cancel();
  client.confirm();
  assert.equal(outgoing.length, 0);
  client.prepare(); await flush();
  document.text = sample.replace("3", "5");
  document.version++;
  await document.onChange();
  client.confirm();
  assert.equal(outgoing.length, 0);
  assert.equal(document.applied.length, 0);
  assert.equal(client.proposal, undefined);
});
test("an older publication and an unrelated proposal cannot replace the visible graph", async () => {
  const { client, editor, document, flush } = connected();
  client.start(); await flush();
  const original = payload<Graph>(editor.lastPublication);
  document.version++;
  await document.onChange();
  assert.equal(client.read(writeToSurface(envelope("publish-graph", "notification", "late", {
    ...original, mode: "complete",
  }), schemas)), false);
  assert.equal(client.session?.graph.context.snapshot.hostVersion, "2");
  assert.equal(client.read(writeToSurface(envelope("edit-result", "response", "unrelated", {
    context: original.context, description: "A different request.", changes: [], documents: [original.context.snapshot],
    mappings: [], diagnostics: [], stage: "proposal", outcome: "proposed",
  }), schemas)), false);
  assert.equal(client.proposal, undefined);
});
test("malformed host messages cross the existing read boundary", () => {
  const { client } = connected();
  assert.equal(client.read({ family: "publish-graph" }), false);
  assert.match(client.notice, /JSON text/);
  assert.equal(client.read("{"), false);
  assert.equal(client.session, undefined);
});
test("page restoration uses a new request prefix while the editor stays alive", async () => {
  const { editor, client, flush } = connected();
  client.start(); await flush();
  const outgoing: string[] = [];
  const restored = new SurfaceClient("page-2", (json) => outgoing.push(json));
  restored.start();
  const result = await editor.handle(outgoing[0]);
  assert.equal(result.accepted, true);
});
test("a delayed preview cannot announce an applied edit or enable confirmation", async () => {
  const { client, editor, outgoing, flush } = connected();
  client.start(); await flush();
  client.prepare();
  const request = readFromSurface(outgoing.shift(), schemas);
  if (!request.accepted) throw new Error(request.reason);
  const graph = payload<Graph>(editor.lastPublication);
  const outdated = { ...graph.context, snapshot: { ...graph.context.snapshot, hostVersion: "0", snapshotId: "earlier" } };
  assert.equal(client.read(writeToSurface(envelope("edit-result", "response", request.message["requestId"] as string, {
    context: outdated, description: "An earlier source preview.", changes: [], documents: [outdated.snapshot],
    mappings: [], diagnostics: [], stage: "proposal", outcome: "proposed",
  }), schemas)), true);
  assert.equal(client.proposal, undefined);
  assert.equal(client.busy, false);
  assert.match(client.notice, /out of date/);
  client.confirm();
  assert.equal(outgoing.length, 0);
});
test("a document refresh cannot settle a missing commit reply or resend the mutation", async () => {
  const outgoing: string[] = [];
  const document = new MemoryDocument();
  const client = new SurfaceClient("page", (json) => outgoing.push(json));
  const editor = new GraphEditor(document, "view", async (json) => {
    const reply = readInbound(json, schemas);
    if (!reply.accepted) throw new Error(reply.reason);
    if (reply.family === "edit-result" && (reply.message["payload"] as unknown as Edit).stage === "commit") return false;
    return client.read(json);
  });
  document.onChange = () => editor.changed();
  client.start(); await editor.handle(outgoing.shift());
  client.prepare(); await editor.handle(outgoing.shift());
  client.confirm(); await editor.handle(outgoing.shift());
  assert.equal(document.applied.length, 1);
  assert.equal(client.session?.graph.context.snapshot.hostVersion, "2");
  assert.equal(client.busy, true);
  assert.match(client.notice, /reply is still pending/);
  client.confirm(); client.prepare(); client.cancel();
  assert.equal(outgoing.length, 0);
  assert.equal(client.busy, true);
});
