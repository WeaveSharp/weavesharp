import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { receiveGraph, describe } from "../src/graph.ts";
import { render } from "../src/render.ts";
import { FakeDocument, elements, repositoryRoot, schemas } from "./support.ts";

const examples = join(repositoryRoot, "tests", "WeaveSharp.Protocol.Tests", "Examples", "publish-graph");
test("both protocol graph fixtures pass through SEC-04 before rendering", () => {
  const completeText = readFileSync(join(examples, "valid-basic.json"), "utf8");
  const complete = receiveGraph(completeText, schemas());
  assert.equal(complete.ok, true);
  if (!complete.ok) return;
  const incrementalText = readFileSync(join(examples, "valid-incremental.json"), "utf8");
  const incremental = receiveGraph(incrementalText, schemas(), { ...complete.graph, revision: 4 });
  assert.equal(incremental.ok, true);
  if (!incremental.ok) return;
  for (const graph of [complete.graph, incremental.graph]) {
    assert.deepEqual(graph.nodes.map((node) => node.id), ["n1"]);
    assert.deepEqual(describe(graph).map((entry) => entry.kind), ["node", "region"]);
    const view = render(new FakeDocument(), graph);
    assert.equal(elements(view.canvas).filter((node) => node.attributes.has("data-ws-key")).length, 2);
  }
  assert.deepEqual(complete.graph.nodes, incremental.graph.nodes);
});

test("incremental rendering needs the matching base and applies removals and updates", () => {
  const message = JSON.parse(readFileSync(join(examples, "valid-incremental.json"), "utf8"));
  const complete = receiveGraph(readFileSync(join(examples, "valid-basic.json"), "utf8"), schemas());
  assert.ok(complete.ok);
  assert.equal(receiveGraph(JSON.stringify(message), schemas()).ok, false);
  assert.equal(receiveGraph(JSON.stringify(message), schemas(), complete.graph).ok, false);
  message.payload.nodes = [{ id: "new-node", kind: "sample-number", typeName: "System.Int32" }];
  message.payload.removedNodeIds = ["n1"];
  message.payload.regions[0].nodeIds = ["new-node"];
  message.payload.mappings = [];
  const result = receiveGraph(JSON.stringify(message), schemas(), { ...complete.graph, revision: 4 });
  assert.ok(result.ok);
  assert.deepEqual(result.graph.nodes.map((node) => node.id), ["new-node"]);
  assert.deepEqual(result.graph.regions[0]!.nodeIds, ["new-node"]);
  assert.equal(complete.graph.nodes[0]!.id, "n1");
});
