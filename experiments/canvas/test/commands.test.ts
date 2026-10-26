import assert from "node:assert/strict";
import { test } from "node:test";
import { KeyboardSession } from "../src/commands.ts";
import { key, pins, type Graph } from "../src/graph.ts";
import { render } from "../src/render.ts";
import { FakeDocument, elements, schemas, shownText } from "./support.ts";
import { publish, sample, sourceSpan } from "./fixtures.ts";

const pinKey = (id: string, name: string) => key("pin", id, name);
function session(graph = sample()) { return new KeyboardSession(graph, schemas()); }
function edit(outbound: string | undefined) {
  assert.ok(outbound);
  const message = JSON.parse(outbound);
  assert.equal(message.family, "prepare-edit");
  assert.equal(message.kind, "query");
  return { command: message.payload.command as string,
    choices: new Map<string, string>(message.payload.choices.map((choice: { name: string; value: string }) => [choice.name, choice.value])), message };
}
function focusValue(model: KeyboardSession, id: string) {
  assert.equal(model.focusNode(id).ok, true);
  assert.equal(model.move("across-next").ok, true);
  assert.equal(model.focus, pinKey(id, "value-output"));
}
function assertRenderedFocus(model: KeyboardSession) {
  const view = render(new FakeDocument(), model.graph, { focus: model.focus, proposals: model.proposals(), announcement: model.announcement });
  for (const root of [view.canvas, view.outline]) {
    const focused = elements(root).filter((entry) => entry.attributes.get("tabindex") === "0");
    assert.equal(focused.length, 1);
    assert.equal(focused[0]!.attributes.get("data-ws-key"), model.focus);
  }
}

test("keyboard journey: insert, inspect, publish, connect and inspect the resulting relationship", () => {
  const model = session();
  const before = JSON.stringify(model.graph);
  assert.equal(model.searchPalette("Number source").length, 1);
  assert.equal(model.beginInsert(model.searchPalette("Number source")[0]!.kind).ok, true);
  assert.match(model.inspect()!.parts.join(" "), /Proposed edit/);
  const insertion = edit(model.commit().outbound);
  assert.equal(insertion.command, "insert-operation");
  assert.equal(JSON.stringify(model.graph), before);
  const addedId = insertion.choices.get("requested-id")!;
  assert.equal(model.focus, key("node", addedId));
  assertRenderedFocus(model);
  const added = { id: addedId, kind: "sample-number", typeName: "System.Int32" };
  model.synchronize(publish({ ...model.graph, nodes: [...model.graph.nodes, added],
    regions: model.graph.regions.map((region) => region.id === "body" ? { ...region, nodeIds: [...region.nodeIds, addedId] } : region),
    mappings: [...model.graph.mappings, { nodeId: addedId, source: sourceSpan(model.graph, 160) }] }, 2));
  assert.equal(model.focus, key("node", addedId));
  assert.match(model.inspect()!.parts.join(" "), /sample-number.*System.Int32/);
  focusValue(model, addedId);
  assert.equal(model.beginConnect().ok, true);
  assert.equal(model.destinations("b").some((item) => item.allowed && item.pin.key === pinKey("b", "value-input")), true);
  assert.equal(model.chooseDestination(pinKey("b", "value-input")).ok, true);
  assert.match(model.inspect()!.parts.join(" "), /Evaluate at the chosen use/);
  const beforeConnect = JSON.stringify(model.graph);
  const connection = edit(model.commit().outbound);
  assert.equal(connection.command, "connect-value");
  assert.equal(connection.choices.get("from-node"), addedId);
  assert.equal(connection.choices.get("to-node"), "b");
  assert.equal(connection.choices.get("source-form"), "expression-at-use");
  const edgeId = connection.choices.get("requested-id")!;
  assert.equal(model.focus, key("connection", edgeId));
  assert.equal(JSON.stringify(model.graph), beforeConnect);
  assertRenderedFocus(model);
  assert.equal(model.commit().ok, false);
  model.synchronize(publish({ ...model.graph, edges: [...model.graph.edges, { id: edgeId, from: addedId, to: "b", kind: "value" }] }, 3));
  assert.equal(model.focus, key("connection", edgeId));
  assert.equal(model.proposals().length, 0);
  assert.equal(model.inspect()!.kind, "connection");
  assertRenderedFocus(model);
});

test("candidate refusals explain direction, role, type, occupancy and scope without changing focus", () => {
  const model = session();
  focusValue(model, "a");
  assert.equal(model.beginConnect().ok, true);
  const origin = model.focus;
  for (const [target, reason] of [
    [pinKey("a", "value-output"), /itself/], [pinKey("d", "value-output"), /input and an output/],
    [pinKey("b", "execution-input"), /different purposes/], [pinKey("f", "value-input"), /region boundary/],
  ] as const) {
    assert.match(model.chooseDestination(target).message, reason);
    assert.equal(model.focus, origin);
  }
  assert.equal(model.commit().ok, false);
  model.cancel();
  focusValue(model, "d");
  model.beginConnect();
  assert.match(model.chooseDestination(pinKey("b", "value-input")).message, /value types differ/);
  model.cancel();
  model.focusEntity(pinKey("c", "execution-output"));
  model.beginConnect();
  assert.match(model.chooseDestination(pinKey("b", "execution-input")).message, /already has/);
});

test("filtering and cancellation retain or restore the originating pin or node", () => {
  const model = session();
  focusValue(model, "a");
  const origin = model.focus;
  model.beginConnect();
  assert.deepEqual(model.destinations("no-such-node"), []);
  assert.equal(model.focus, origin);
  model.chooseDestination(pinKey("c", "value-input"));
  model.cancel();
  assert.equal(model.focus, origin);
  assert.equal(model.proposals().length, 0);
  model.beginInsert("sample-step");
  model.cancel();
  assert.equal(model.focus, origin);
  assertRenderedFocus(model);
});

test("host refusal is announced and a keyboard retry can select another destination", () => {
  const model = session();
  const before = JSON.stringify(model.graph);
  focusValue(model, "a");
  const origin = model.focus;
  model.beginConnect();
  model.chooseDestination(pinKey("b", "value-input"));
  model.commit();
  assert.equal(model.rejectPending("The argument would be evaluated twice.").ok, false);
  assert.equal(model.focus, origin);
  assert.equal(JSON.stringify(model.graph), before);
  const view = render(new FakeDocument(), model.graph, { focus: model.focus, announcement: model.announcement });
  const status = elements(view.outline).find((node) => node.attributes.get("role") === "status")!;
  assert.match(shownText(status), /evaluated twice/);
  assert.equal(model.beginConnect().ok, true);
  assert.equal(model.chooseDestination(pinKey("c", "value-input")).ok, true);
  assert.equal(model.commit().ok, true);
});

test("reconnect preserves the existing edge until publication and disconnect requires a replacement", () => {
  const initial = sample();
  const model = session(publish({ ...initial, edges: [...initial.edges, { id: "value-1", from: "a", to: "b", kind: "value" }] }));
  model.focusEntity(key("connection", "value-1"));
  assert.equal(model.beginReconnect("value-1").ok, true);
  model.chooseDestination(pinKey("c", "value-input"));
  const reconnect = edit(model.commit().outbound);
  assert.equal(reconnect.choices.get("replace-relationship"), "value-1");
  assert.equal(model.graph.edges.some((edge) => edge.id === "value-1"), true);
  const newId = reconnect.choices.get("requested-id")!;
  model.synchronize(publish({ ...model.graph, edges: [...initial.edges, { id: newId, from: "a", to: "c", kind: "value" }] }, 2));
  assert.equal(model.focus, key("connection", newId));
  assert.equal(model.beginDisconnect(newId, "").ok, false);
  assert.equal(model.beginDisconnect(newId, "42").ok, true);
  const disconnect = edit(model.commit().outbound);
  assert.equal(disconnect.command, "disconnect-value");
  assert.equal(disconnect.choices.get("replacement-expression"), "42");
  assert.equal(model.focus, pinKey("c", "value-input"));
  assert.equal(model.graph.edges.some((edge) => edge.id === newId), true);
  model.synchronize(publish({ ...model.graph, edges: initial.edges }, 3));
  assert.equal(model.focus, pinKey("c", "value-input"));
  assert.match(model.inspect()!.parts.join(" "), /Unconnected/);
});

test("removal moves focus into the closest surviving region and announces the change", () => {
  const model = session();
  model.focusEntity(pinKey("f", "value-input"));
  model.synchronize(publish({ ...model.graph, nodes: model.graph.nodes.filter((node) => node.id !== "f"),
    regions: model.graph.regions.map((region) => ({ ...region, nodeIds: region.nodeIds.filter((id) => id !== "f") })),
    mappings: model.graph.mappings.filter((mapping) => mapping.nodeId !== "f") }, 2));
  assert.equal(model.focus, key("region", "inner"));
  assert.match(model.announcement, /removed.*surviving region/);
  assertRenderedFocus(model);
  model.synchronize(publish({ ...model.graph, nodes: [], regions: [], edges: [], mappings: [], diagnostics: [] }, 3));
  assert.equal(model.focus, undefined);
  const views = render(new FakeDocument(), model.graph, { announcement: model.announcement });
  assert.equal(elements(views.outline).find((node) => node.attributes.get("role") === "tree")!.attributes.get("tabindex"), "0");
});

test("source refresh cancels an obsolete proposal and separates selection from focus", () => {
  const model = session();
  focusValue(model, "a");
  const origin = model.focus;
  model.expandSelection();
  assert.equal(model.focus, origin);
  assert.equal(model.selected.has(key("node", "b")), true);
  model.toggleRegion("body");
  assert.equal(model.focus, origin);
  model.toggleRegion("body");
  model.beginConnect();
  model.chooseDestination(pinKey("c", "value-input"));
  model.synchronize(publish(model.graph, 2));
  assert.equal(model.focus, origin);
  assert.match(model.announcement, /proposal was cleared/);
  assert.equal(model.commit().ok, false);
  model.enterRegion("inner");
  assert.equal(model.focus, key("node", "f"));
});

test("input-led connection and execution connections use the same logical commands", () => {
  const model = session();
  model.focusEntity(pinKey("c", "execution-input"));
  model.beginConnect();
  model.chooseDestination(pinKey("b", "execution-output"));
  const result = edit(model.commit().outbound);
  assert.equal(result.command, "connect-execution");
  assert.equal(result.choices.get("from-node"), "b");
  assert.equal(result.choices.get("to-node"), "c");
  model.cancel();
  assert.equal(model.focus, pinKey("c", "execution-input"));
  model.focusNode("a");
  model.move("next");
  assert.equal(model.focus, key("node", "b"));
  model.move("previous");
  assert.equal(model.focus, key("node", "a"));
  assert.equal(pins(model.graph.nodes[0]!)[0]!.name, "value-output");
});

test("missing source locations and stale insertion sites refuse commit without a graph edit", () => {
  const initial = sample();
  const model = session(publish({ ...initial, mappings: [] }));
  assert.equal(model.beginInsert("sample-number").ok, false);
  focusValue(model, "a");
  model.beginConnect();
  model.chooseDestination(pinKey("b", "value-input"));
  assert.match(model.commit().message, /source locations/);
  model.cancel();
  model.beginInsert("sample-number", { ...sourceSpan(initial, 0), snapshot: { ...initial.context.snapshot, snapshotId: "old" } });
  assert.match(model.commit().message, /older source snapshot/);
  assert.equal(model.graph.nodes.length, initial.nodes.length);
});

test("an unrelated publication does not claim a pending disconnection was applied", () => {
  const base = sample();
  const model = session(publish({ ...base, edges: [...base.edges, { id: "value-1", from: "a", to: "b", kind: "value" }] }));
  model.beginDisconnect("value-1", "42");
  model.commit();
  model.synchronize(publish(model.graph, 2));
  assert.equal(model.focus, key("connection", "value-1"));
  assert.match(model.announcement, /proposal was cleared/);
  assert.equal(model.graph.edges.some((edge) => edge.id === "value-1"), true);
});

test("an isolation-layer refusal preserves the proposal and its focus for recovery", () => {
  const base = sample();
  const model = session(publish({ ...base, edges: [...base.edges, { id: "value-1", from: "a", to: "b", kind: "value" }] }));
  model.beginDisconnect("value-1", "1".repeat(4097));
  const result = model.commit();
  assert.equal(result.ok, false);
  assert.equal(result.outbound, undefined);
  assert.equal(model.focus, key("connection", "value-1"));
  assert.match(model.inspect()!.parts.join(" "), /Proposed edit/);
  assert.equal(model.cancel().ok, true);
});

test("snapshot comparison does not depend on JSON property order", () => {
  const model = session();
  const snapshot = model.graph.context.snapshot;
  model.beginInsert("sample-number", { ...sourceSpan(model.graph, 0), snapshot: {
    snapshotId: snapshot.snapshotId, hostVersion: snapshot.hostVersion, documentUri: snapshot.documentUri, hostId: snapshot.hostId,
  } });
  assert.equal(model.commit().ok, true);
});

test("removal during insertion returns to a surviving region, including removal of a nested region", () => {
  const model = session();
  model.focusNode("f");
  model.beginInsert("sample-number");
  model.synchronize(publish({ ...model.graph, nodes: model.graph.nodes.filter((node) => node.id !== "f"),
    regions: model.graph.regions.map((region) => ({ ...region, nodeIds: region.nodeIds.filter((id) => id !== "f") })),
    mappings: model.graph.mappings.filter((mapping) => mapping.nodeId !== "f") }, 2));
  assert.equal(model.focus, key("region", "inner"));
  const nested = session();
  nested.focusEntity(key("region", "inner"));
  nested.synchronize(publish({ ...nested.graph, regions: nested.graph.regions.filter((region) => region.id !== "inner") }, 2));
  assert.equal(nested.focus, key("region", "body"));
});

test("switching graphs clears local focus and selection even when node ids happen to match", () => {
  const model = session();
  model.expandSelection();
  model.beginInsert("sample-number");
  model.synchronize(publish({ ...model.graph, graphId: "another-body" }));
  assert.equal(model.focus, undefined);
  assert.equal(model.selected.size, 0);
  assert.equal(model.proposals().length, 0);
  assert.match(model.announcement, /different graph/);
});
