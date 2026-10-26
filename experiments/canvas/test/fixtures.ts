import { receiveGraph, type Graph, type SourceNode, type Span } from "../src/graph.ts";
import { schemas } from "./support.ts";

const schemaSet = schemas();
export function publish(graph: Graph, revision = graph.revision): Graph {
  const read = receiveGraph(JSON.stringify({ version: { major: 0, minor: 0 }, family: "publish-graph", kind: "notification",
    requestId: `publication-${revision}`, payload: { ...graph, revision, mode: "complete" }, extensions: {} }), schemaSet);
  if (!read.ok) throw new Error(read.reason);
  return read.graph;
}

export function sourceSpan(graph: Graph, start: number): Span {
  return { snapshot: graph.context.snapshot, coordinates: "utf16-offset", start, length: 10 };
}

export function sample(): Graph {
  const context = { workspaceId: "lab", projectId: "demo", compilationContextId: "demo-context",
    snapshot: { hostId: "fixture-host", documentUri: "workspace://lab/Keyboard.cs", hostVersion: "1", snapshotId: "sample-1" } };
  const nodes: SourceNode[] = [
    { id: "a", kind: "sample-number", typeName: "System.Int32" },
    { id: "b", kind: "sample-display", typeName: "System.Int32" },
    { id: "c", kind: "sample-display", typeName: "System.Int32" },
    { id: "d", kind: "sample-text", typeName: "System.String" },
    { id: "e", kind: "sample-step", typeName: "" },
    { id: "f", kind: "sample-display", typeName: "System.Int32" },
  ];
  const mappings = nodes.map((node, index) => ({ nodeId: node.id,
    source: { snapshot: context.snapshot, coordinates: "utf16-offset" as const, start: index * 20, length: 10 } }));
  return publish({ context, graphId: "keyboard-body", revision: 1, nodes,
    edges: [{ id: "flow-1", from: "e", to: "b", kind: "execution" }],
    regions: [{ id: "body", kind: "body", nodeIds: nodes.map((node) => node.id) }, { id: "inner", kind: "branch", nodeIds: ["f"] }],
    mappings, diagnostics: [{ code: "DEMO1", severity: "info", message: "This is a sample operation.", source: mappings[1]!.source }], coverage: "complete" });
}

export function chain(count: number): Graph {
  const base = sample();
  const nodes = Array.from({ length: count }, (_, index) => ({ id: `step-${index}`, kind: "sample-step", typeName: "" }));
  return publish({ ...base, nodes, edges: nodes.slice(1).map((node, index) => ({ id: `link-${index}`, from: nodes[index]!.id, to: node.id, kind: "execution" })),
    regions: [{ id: "chain-body", kind: "body", nodeIds: nodes.map((node) => node.id) }],
    mappings: nodes.map((node, index) => ({ nodeId: node.id, source: sourceSpan(base, index * 20) })), diagnostics: [] });
}
