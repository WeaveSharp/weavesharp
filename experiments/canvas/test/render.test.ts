import assert from "node:assert/strict";
import { test } from "node:test";
import { key } from "../src/graph.ts";
import { render } from "../src/render.ts";
import { FakeDocument, elements, shownText, type FakeElement } from "./support.ts";
import { publish, sample } from "./fixtures.ts";

const identities = (root: FakeElement) => elements(root).filter((node) => node.attributes.has("data-ws-key"))
  .map((node) => Object.fromEntries([...node.attributes].filter(([name]) => name.startsWith("data-ws-"))))
  .sort((a, b) => (a["data-ws-key"] ?? "").localeCompare(b["data-ws-key"] ?? ""));

test("outline and canvas expose all expected nodes, pins, connections and regions with identical identities", () => {
  const original = sample();
  const graph = publish({ ...original, edges: [...original.edges, { id: "value-1", from: "a", to: "b", kind: "value" }] });
  const view = render(new FakeDocument(), graph, { focus: key("node", "b"), selected: new Set([key("node", "a")]) });
  const canvas = identities(view.canvas as FakeElement);
  const outline = identities(view.outline as FakeElement);
  assert.deepEqual(outline, canvas);
  const expected = [
    ...["a", "b", "c", "d", "e", "f"].map((id) => key("node", id)),
    key("pin", "a", "value-output"), key("pin", "d", "value-output"),
    ...["b", "c", "f"].flatMap((id) => ["execution-input", "value-input", "execution-output"].map((name) => key("pin", id, name))),
    key("pin", "e", "execution-input"), key("pin", "e", "execution-output"),
    key("connection", "flow-1"), key("connection", "value-1"), key("region", "body"), key("region", "inner"),
  ];
  assert.deepEqual(new Set(canvas.map((item) => item["data-ws-key"])), new Set(expected));
  assert.equal(canvas.length, expected.length);
  for (const root of [view.canvas, view.outline]) {
    assert.deepEqual(elements(root).filter((node) => node.attributes.has("data-ws-reference"))
      .map((node) => node.attributes.get("data-ws-reference")), graph.regions.flatMap((region) => region.nodeIds.map((id) => key("node", id))));
  }
  const edge = outline.find((item) => item["data-ws-id"] === "value-1")!;
  assert.equal(edge["data-ws-from"], key("pin", "a", "value-output"));
  assert.equal(edge["data-ws-to"], key("pin", "b", "value-input"));
});

test("tree nesting, source order, selection, type, diagnostics, passing mode and actions are exposed", () => {
  const view = render(new FakeDocument(), sample(), { focus: key("pin", "b", "value-input"), selected: new Set([key("node", "a")]), collapsed: new Set([key("region", "inner")]) });
  const items = elements(view.outline);
  const tree = items.find((node) => node.attributes.get("role") === "tree")!;
  assert.deepEqual(tree.children.filter((node) => node.kind === "element" && node.attributes.get("data-ws-kind") === "node")
    .map((node) => (node as FakeElement).attributes.get("data-ws-id")), ["a", "b", "c", "d", "e", "f"]);
  const nodeB = items.find((node) => node.attributes.get("data-ws-key") === key("node", "b"))!;
  assert.equal(nodeB.attributes.get("aria-level"), "1");
  assert.equal(nodeB.attributes.get("aria-expanded"), "true");
  assert.match(shownText(nodeB), /System.Int32.*DEMO1.*Actions:/);
  const pinB = items.find((node) => node.attributes.get("data-ws-key") === key("pin", "b", "value-input"))!;
  assert.equal(pinB.attributes.get("aria-level"), "2");
  assert.equal(pinB.attributes.get("tabindex"), "0");
  assert.equal(pinB.attributes.get("aria-selected"), "false");
  assert.match(shownText(pinB), /input.*value.*Passing: value.*Unconnected/);
  assert.equal(items.find((node) => node.attributes.get("data-ws-key") === key("node", "a"))!.attributes.get("aria-selected"), "true");
  const inner = items.find((node) => node.attributes.get("data-ws-key") === key("region", "inner"))!;
  assert.equal(inner.attributes.get("aria-expanded"), "false");
  assert.ok(elements(inner).some((node) => node.attributes.get("role") === "group" && node.attributes.has("hidden")));
});

test("SVG carries labelled geometry while all visible labels stay in valid HTML namespaces", () => {
  const base = sample();
  const graph = publish({ ...base, nodes: base.nodes.map((node, index) => index === 0 ? { ...node, typeName: "عدد\u202eabc" } : node),
    edges: [...base.edges, { id: "value-1", from: "a", to: "b", kind: "value" }] });
  const document = new FakeDocument();
  const view = render(document, graph);
  const all = [...elements(view.canvas), ...elements(view.outline)];
  const byId = new Map(all.filter((node) => node.attributes.has("id")).map((node) => [node.attributes.get("id"), node]));
  assert.equal(byId.size, all.filter((node) => node.attributes.has("id")).length);
  for (const node of all) {
    const target = node.attributes.get("aria-labelledby");
    if (target !== undefined) assert.ok(byId.has(target));
    if (node.namespace === "http://www.w3.org/2000/svg") {
      assert.ok(node.children.every((child) => child.kind === "element" && child.namespace === node.namespace));
    }
    for (const child of node.children) if (child.kind === "text" && child.data.trim() !== "·") {
      assert.ok(node.attributes.get("class")?.includes("ws-label") || node.attributes.get("class") === "ws-hidden-characters");
    }
  }
  const labels = all.filter((node) => node.attributes.get("class")?.split(" ").includes("ws-label"));
  assert.ok(labels.length > 0);
  assert.ok(labels.every((node) => node.attributes.get("dir") === "auto"));
  for (const root of [view.canvas, view.outline]) {
    const text = shownText(root as FakeElement);
    assert.ok(!text.includes("\u202e"));
    assert.match(text, /U\+202E/);
    assert.ok(elements(root).some((node) => node.attributes.get("class") === "ws-hidden-characters"));
  }
  const geometry = elements(view.canvas);
  assert.ok(geometry.some((node) => node.name === "circle" && node.attributes.get("class") === "pin-value"));
  assert.ok(geometry.some((node) => node.name === "path" && node.attributes.get("class") === "pin-execution"));
  const execution = geometry.find((node) => node.attributes.get("class") === "connection-execution")!;
  const value = geometry.find((node) => node.attributes.get("class") === "connection-value")!;
  assert.equal((execution.attributes.get("d")!.match(/M/g) ?? []).length, 1);
  assert.equal((value.attributes.get("d")!.match(/M/g) ?? []).length, 2);
  assert.match(shownText(view.canvas as FakeElement), /execution/);
  assert.match(shownText(view.canvas as FakeElement), /value/);
});

test("identity keys distinguish punctuation, repeated display kinds and different entity categories", () => {
  const base = sample();
  const unusual = publish({ ...base, nodes: [{ id: "body", kind: "return", typeName: "" }, { id: "a:b", kind: "return", typeName: "" }, { id: "a", kind: "return", typeName: "" }],
    regions: [{ id: "body", kind: "body", nodeIds: ["body", "a:b", "a"] }], edges: [], mappings: [], diagnostics: [] });
  const views = render(new FakeDocument(), unusual);
  assert.equal(new Set(identities(views.canvas as FakeElement).map((item) => item["data-ws-key"])).size, 4);
  assert.deepEqual(identities(views.canvas as FakeElement), identities(views.outline as FakeElement));
});
