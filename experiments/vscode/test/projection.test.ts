import assert from "node:assert/strict";
import { test } from "node:test";
import { pins } from "../../canvas/src/graph.ts";
import { projectStandIn } from "../src/stand-in.ts";
import { planInsertion } from "../src/edits.ts";
import { insertionRequest } from "../src/wire.ts";
import type { Preparation } from "../src/wire.ts";
import { rig, sample } from "./support.ts";

test("stand-in spans follow UTF-16 source offsets and palette execution pins", () => {
  const source = "// 🧭 heading\r\n" + sample.replaceAll("\n", "\r\n");
  const { editor } = rig(source);
  const projection = projectStandIn(source, editor.context, 7);
  assert.equal(projection.editable, true);
  assert.equal(projection.graph.nodes.length, 3);
  assert.equal(projection.graph.revision, 7);
  assert.equal(projection.statements[0]?.source.start, source.indexOf("int total"));
  for (const [index, statement] of projection.statements.entries()) {
    assert.equal(source.slice(statement.source.start, statement.source.start + statement.source.length), statement.text);
    assert.deepEqual(statement.source.snapshot, editor.context.snapshot);
    assert.equal(pins(projection.graph.nodes[index]!).filter((pin) => pin.role === "execution").length, 2);
  }
  assert.deepEqual(projection.graph.edges.map((edge) => [edge.from, edge.to]), [
    [projection.graph.nodes[0]!.id, projection.graph.nodes[1]!.id],
    [projection.graph.nodes[1]!.id, projection.graph.nodes[2]!.id],
  ]);
});
test("unknown statements break the displayed chain and disable editing", () => {
  const source = sample.replace("total++;", "Console.WriteLine(total);");
  const result = projectStandIn(source, rig().editor.context, 1);
  assert.equal(result.editable, false);
  assert.equal(result.graph.coverage, "partial");
  assert.equal(result.graph.nodes.length, 2);
  assert.equal(result.graph.edges.length, 0);
  assert.equal(result.graph.diagnostics[0]?.code, "stand-in-statement");
});
test("unclosed blocks and multiple methods are outside the sample shape", () => {
  for (const source of [sample.slice(0, -4), "class D { void A() {} void B() {} }", "class D { void A() { if (true) {} } }"]) {
    const result = projectStandIn(source, rig().editor.context, 1);
    assert.equal(result.graph.coverage, "unavailable");
    assert.equal(result.editable, false);
    assert.deepEqual(result.graph.nodes, []);
  }
});
test("unfinished statement and return do not imply an execution connection", () => {
  const context = rig().editor.context;
  const tail = projectStandIn("class D { void A() { int x = 1; x++ } }", context, 1);
  assert.equal(tail.graph.coverage, "partial");
  assert.equal(tail.graph.diagnostics[0]?.code, "stand-in-tail");
  const terminal = projectStandIn("class D { void A() { return; ; } }", context, 1);
  assert.equal(terminal.graph.nodes.length, 2);
  assert.equal(terminal.graph.edges.length, 0);
});
test("a no-op insertion preserves CRLF, indentation and the rest of the source", () => {
  const source = sample.replaceAll("\n", "\r\n");
  const { editor } = rig(source);
  const anchor = projectStandIn(source, editor.context, 1).statements[1]!.source;
  const request = insertionRequest("p", editor.context, anchor).payload as Preparation;
  const plan = planInsertion(source, editor.context, request);
  assert.notEqual(typeof plan, "string");
  if (typeof plan === "string") throw new Error(plan);
  assert.equal(plan.changes.length, 1);
  assert.equal(plan.changes[0]!.text, ";\r\n        ");
  const change = plan.changes[0]!;
  const after = source.slice(0, change.span.start) + change.text + source.slice(change.span.start);
  assert.equal(after, source.replace("total++;", ";\r\n        total++;"));
  assert.equal(projectStandIn(after, editor.context, 2).graph.nodes.length, 4);
});
test("planning refuses unsupported operations and anchors not owned by statements", () => {
  const { editor } = rig();
  const anchor = projectStandIn(sample, editor.context, 1).statements[0]!.source;
  const request = insertionRequest("p", editor.context, anchor).payload as Preparation;
  assert.match(planInsertion(sample, editor.context, { ...request, command: "connect-execution" }) as string, /Only/);
  assert.match(planInsertion(sample, editor.context, { ...request, anchors: [{ ...anchor, start: 0 }] }) as string, /current statement/);
  assert.match(planInsertion(sample, editor.context, { ...request, destinations: [anchor.snapshot, anchor.snapshot] }) as string, /one document/);
});
