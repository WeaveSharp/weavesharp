import assert from "node:assert/strict";
import { test } from "node:test";
import { arrowCommand, layout, mapBox, mapPoint, pinPosition, size, type Flow } from "../src/layout.ts";
import { pins } from "../src/graph.ts";
import { chain, publish, sample } from "./fixtures.ts";

test("layout is deterministic, source-stable and independent of edge enumeration", () => {
  const graph = sample();
  const before = JSON.stringify(graph);
  const first = layout(graph);
  assert.deepEqual(layout(graph), first);
  assert.deepEqual(layout({ ...graph, edges: [...graph.edges].reverse() }), first);
  assert.deepEqual([...first.positions.keys()], graph.nodes.map((node) => node.id));
  assert.equal(new Set([...first.positions.values()].map((point) => JSON.stringify(point))).size, graph.nodes.length);
  assert.equal(JSON.stringify(graph), before);
  const sequence = layout(chain(3));
  assert.deepEqual([...sequence.positions.values()], [
    { along: 40, across: 40 }, { along: 280, across: 40 }, { along: 520, across: 40 },
  ]);
});

test("cycles, disconnected nodes, empty input and reordered connections have bounded placements", () => {
  const graph = chain(4);
  const loop = publish({ ...graph, edges: [...graph.edges, { id: "loop", from: "step-3", to: "step-0", kind: "execution" }] });
  assert.deepEqual(layout(loop), layout(graph));
  for (const position of layout(sample()).positions.values()) {
    assert.ok(Number.isFinite(position.along) && Number.isFinite(position.across));
  }
  assert.deepEqual([...layout({ ...graph, nodes: [], edges: [] }).positions], []);
});

test("three physical mappings preserve logical coordinates and mirror exactly", () => {
  const graph = chain(3);
  const placement = layout(graph);
  const before = [...placement.positions];
  for (const position of placement.positions.values()) {
    const ltr = mapPoint(position, placement.extent, "left-to-right");
    const rtl = mapPoint(position, placement.extent, "right-to-left");
    const down = mapPoint(position, placement.extent, "top-to-bottom");
    assert.deepEqual(ltr, { x: position.along, y: position.across });
    assert.deepEqual(rtl, { x: placement.extent.along - position.along, y: position.across });
    assert.deepEqual(down, { x: position.across, y: position.along });
    assert.equal(ltr.x + rtl.x, placement.extent.along);
    assert.deepEqual(mapPoint({ along: rtl.x, across: rtl.y }, placement.extent, "right-to-left"), ltr);
    const a = mapBox(position, size, placement.extent, "left-to-right");
    const b = mapBox(position, size, placement.extent, "right-to-left");
    const c = mapBox(position, size, placement.extent, "top-to-bottom");
    assert.equal(a.x + b.x + a.width, placement.extent.along);
    assert.deepEqual(c, { x: a.y, y: a.x, width: a.height, height: a.width });
  }
  assert.deepEqual([...placement.positions], before);
});

test("logical pin sides map to left/right, right/left and top/bottom", () => {
  const graph = sample();
  const placement = layout(graph);
  for (const node of graph.nodes) for (const pin of pins(node)) {
    const logical = pinPosition(graph, placement, pin);
    assert.ok(Number.isInteger(logical.across));
    assert.equal(pin.side, pin.direction === "input" ? "leading" : "trailing");
    for (const direction of ["left-to-right", "right-to-left", "top-to-bottom"] as const) {
      const point = mapPoint(logical, placement.extent, direction);
      const box = mapBox(placement.positions.get(node.id)!, size, placement.extent, direction);
      if (direction === "left-to-right") assert.equal(point.x, box.x + (pin.side === "trailing" ? box.width : 0));
      if (direction === "right-to-left") assert.equal(point.x, box.x + (pin.side === "leading" ? box.width : 0));
      if (direction === "top-to-bottom") assert.equal(point.y, box.y + (pin.side === "trailing" ? box.height : 0));
    }
  }
});

test("arrow bindings cover along and across movement in every direction", () => {
  const expected: Record<Flow, readonly string[]> = {
    "left-to-right": ["previous", "next", "across-previous", "across-next"],
    "right-to-left": ["next", "previous", "across-previous", "across-next"],
    "top-to-bottom": ["across-previous", "across-next", "previous", "next"],
  };
  for (const direction of Object.keys(expected) as Flow[]) {
    assert.deepEqual(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].map((arrow) => arrowCommand(arrow, direction)), expected[direction]);
    assert.equal(arrowCommand("Enter", direction), undefined);
  }
});
