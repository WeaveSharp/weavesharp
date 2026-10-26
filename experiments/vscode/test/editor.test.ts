import assert from "node:assert/strict";
import { test } from "node:test";
import { readFromSurface, readInbound, writeOutbound } from "../../../src/surface/src/isolation/index.ts";
import type { Graph } from "../../canvas/src/graph.ts";
import { GraphEditor } from "../src/editor.ts";
import { schemas } from "../src/schemas.generated.ts";
import { commitRequest, envelope, graphRequest, insertionRequest, type Edit } from "../src/wire.ts";
import { MemoryDocument, payload, prepare, rig, sample } from "./support.ts";

test("prepare, one apply, simulated undo and redo publish the current model version", async () => {
  const { editor, document, posted } = rig();
  assert.equal((await editor.handle(writeOutbound(graphRequest("opening"), schemas))).accepted, true);
  const proposal = await prepare(editor);
  assert.equal(document.text, sample);
  assert.equal(document.applied.length, 0);
  const result = await editor.handle(writeOutbound(commitRequest("commit-1", proposal), schemas));
  assert.equal(result.outcome, "applied");
  assert.equal(document.applied.length, 1);
  assert.equal(document.applied[0]?.version, 1);
  assert.equal(document.applied[0]?.changes.length, 1);
  const edited = document.text;
  assert.notEqual(edited, sample);
  assert.equal(payload<Graph>(editor.lastPublication).nodes.length, 4);
  await document.undo();
  assert.equal(document.text, sample);
  assert.equal(document.version, 3);
  assert.equal(payload<Graph>(editor.lastPublication).nodes.length, 3);
  await document.redo();
  assert.equal(document.text, edited);
  assert.equal(document.version, 4);
  assert.equal(payload<Graph>(editor.lastPublication).context.snapshot.hostVersion, "4");
  for (const json of posted) assert.equal(readInbound(json, schemas).accepted, true);
});
test("stale preparation and commit never reach the document transaction", async () => {
  const { editor, document } = rig();
  const proposal = await prepare(editor);
  const anchor = payload<Graph>(editor.lastPublication).mappings[0]!.source;
  document.version++;
  await editor.changed();
  const stalePrepare = insertionRequest("old-prepare", proposal.context, anchor);
  assert.match((await editor.handle(writeOutbound(stalePrepare, schemas))).reason!, /Stale/);
  assert.match((await editor.handle(writeOutbound(commitRequest("old-commit", proposal), schemas))).reason!, /Stale/);
  assert.equal(document.text, sample);
  assert.equal(document.applied.length, 0);
});
test("wrong host, document, destination, change snapshot and context are refused", async () => {
  for (const location of ["host", "document", "destination", "change", "context", "token"] as const) {
    const { editor, document } = rig();
    const plan = await prepare(editor);
    const foreign = { ...plan.context.snapshot, documentUri: "file:///elsewhere/Other.cs" };
    const changed: Edit = location === "host" ? { ...plan, context: { ...plan.context, snapshot: { ...plan.context.snapshot, hostId: "another-host" } } } :
      location === "document" ? { ...plan, context: { ...plan.context, snapshot: foreign } } :
      location === "destination" ? { ...plan, documents: [foreign] } :
      location === "change" ? { ...plan, changes: plan.changes.map((change) => ({ ...change, span: { ...change.span, snapshot: foreign } })) } :
      location === "context" ? { ...plan, context: { ...plan.context, projectId: "another-project" } } :
      { ...plan, context: { ...plan.context, snapshot: { ...plan.context.snapshot, snapshotId: "obsolete" } } };
    const result = await editor.handle(writeOutbound(commitRequest("foreign", changed), schemas));
    assert.equal(result.outcome, "rejected", location);
    assert.match(result.reason!, /Wrong|context|snapshot/);
    assert.equal(document.applied.length, 0);
  }
});
test("foreign preparation is refused even when its anchor belongs to this editor", async () => {
  const { editor, document } = rig();
  await editor.publish();
  const graph = payload<Graph>(editor.lastPublication);
  const foreign = { ...graph.context, snapshot: { ...graph.context.snapshot, documentUri: "file:///foreign.cs" } };
  const result = await editor.handle(writeOutbound(insertionRequest("foreign", foreign, graph.mappings[0]!.source), schemas));
  assert.match(result.reason!, /Wrong document/);
  assert.equal(document.applied.length, 0);
});
test("a commit must preserve the host's prepared text and source range", async () => {
  for (const alteration of ["text", "range"]) {
    const { editor, document } = rig();
    const plan = await prepare(editor);
    const changed = { ...plan, changes: plan.changes.map((change) => alteration === "text" ? { ...change, text: "total = 8;" } :
      { ...change, span: { ...change.span, start: 0 } }) };
    assert.match((await editor.handle(writeOutbound(commitRequest("changed", changed), schemas))).reason!, /exact change/);
    assert.equal(document.applied.length, 0);
  }
});
test("a refused commit retires the preview even if the original document is still current", async () => {
  const { editor, document } = rig();
  const plan = await prepare(editor);
  const foreign = { ...plan, documents: [{ ...plan.context.snapshot, documentUri: "file:///another.cs" }] };
  assert.match((await editor.handle(writeOutbound(commitRequest("wrong-destination", foreign), schemas))).reason!, /Wrong document/);
  assert.match((await editor.handle(writeOutbound(commitRequest("reuse-preview", plan), schemas))).reason!, /Prepare/);
  assert.equal(document.applied.length, 0);
  const refreshed = await prepare(editor, "prepare-again");
  assert.equal((await editor.handle(writeOutbound(commitRequest("fresh-preview", refreshed), schemas))).outcome, "applied");
  assert.equal(document.applied.length, 1);
});
test("SEC-04 refuses malformed, nontext, forged-family and extra-field input", async () => {
  const { editor, document, posted } = rig();
  const valid = graphRequest("opening");
  for (const raw of [valid, "{", "[]", JSON.stringify({ ...valid, family: "publish-graph" }),
    JSON.stringify({ ...valid, surplus: true }), JSON.stringify({ ...valid, payload: {} }),
    '{"constructor":{},"family":"request-graph","kind":"query"}']) {
    const boundary = readFromSurface(raw, schemas);
    assert.equal(boundary.accepted, false);
    assert.deepEqual(await editor.handle(raw), { accepted: false, reason: !boundary.accepted ? boundary.reason : "" });
  }
  assert.equal(document.applied.length, 0);
  assert.deepEqual(posted, []);
});
test("foreign graph reads select no file and unsupported layout messages cause no effect", async () => {
  const { editor, document } = rig();
  const context = { ...editor.context, snapshot: { ...editor.context.snapshot, documentUri: "file:///elsewhere/Secret.cs" } };
  assert.match((await editor.handle(writeOutbound(graphRequest("foreign-read", context), schemas))).reason!, /Wrong document/);
  const layout = envelope("read-or-save-layout", "query", "layout-read", {
    schemaVersion: 1, context: editor.context, view: "personal", operation: "read", revision: "unused",
    positions: [], conflicts: [],
  });
  assert.match((await editor.handle(writeOutbound(layout, schemas))).reason!, /not implemented/);
  assert.equal(editor.lastPublication, undefined);
  assert.equal(document.applied.length, 0);
});
test("duplicate and replayed commits cannot apply twice", async () => {
  const { editor, document } = rig();
  const plan = await prepare(editor);
  const json = writeOutbound(commitRequest("one-commit", plan), schemas);
  assert.equal((await editor.handle(json)).outcome, "applied");
  assert.match((await editor.handle(json)).reason!, /already/);
  assert.match((await editor.handle(writeOutbound(commitRequest("replay", plan), schemas))).reason!, /Stale/);
  assert.equal(document.applied.length, 1);
});
test("a version change at the adapter boundary prevents application", async () => {
  const { editor, document } = rig();
  const plan = await prepare(editor);
  document.beforeApply = () => { document.version++; };
  assert.match((await editor.handle(writeOutbound(commitRequest("racing", plan), schemas))).reason!, /Stale/);
  assert.equal(document.applied.length, 0);
  assert.equal(document.text, sample);
});
test("false and throwing applies return a refusal and consume the proposal", async () => {
  for (const failure of ["failed", "throw"] as const) {
    const { editor, document } = rig();
    const plan = await prepare(editor);
    document.failure = failure;
    assert.match((await editor.handle(writeOutbound(commitRequest("failure", plan), schemas))).reason!, /did not apply/);
    document.failure = undefined;
    assert.match((await editor.handle(writeOutbound(commitRequest("retry", plan), schemas))).reason!, /Prepare/);
    assert.equal(document.applied.length, 0);
  }
});
test("parallel commits are serialized without a mutation retry", async () => {
  const document = new MemoryDocument();
  let settle: (outcome: "failed") => void = () => {};
  let calls = 0;
  document.apply = async () => { calls++; return new Promise<"failed">((resolve) => { settle = resolve; }); };
  const editor = new GraphEditor(document, "test-view", async () => true);
  const plan = await prepare(editor);
  const first = editor.handle(writeOutbound(commitRequest("first", plan), schemas));
  const second = await editor.handle(writeOutbound(commitRequest("second", plan), schemas));
  assert.match(second.reason!, /still being applied/);
  settle("failed");
  await first;
  assert.equal(calls, 1);
});
test("lost delivery never retries an edit and disposal stops subsequent writes", async () => {
  const document = new MemoryDocument();
  const editor = new GraphEditor(document, "test-view", async () => false);
  const plan = await prepare(editor);
  await editor.handle(writeOutbound(commitRequest("commit", plan), schemas));
  assert.equal(document.applied.length, 1);
  assert.match(editor.issue!, /delivery/);
  editor.dispose();
  assert.match((await editor.handle(writeOutbound(graphRequest("after-close"), schemas))).reason!, /closed/);
});
