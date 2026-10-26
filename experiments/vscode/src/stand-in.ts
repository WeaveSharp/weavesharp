import type { Context, Graph, Span } from "../../canvas/src/graph.ts";

export interface Statement { readonly source: Span; readonly text: string; readonly terminal: boolean }
export interface StandIn { readonly graph: Graph; readonly statements: readonly Statement[]; readonly editable: boolean }

// This is a syntax demonstration, with no binding or compilation service.
// It accepts one class containing one parameterless void method.
const methodShape = /^\s*(?:\/\/[^\r\n]*\r?\n\s*)*class\s+[A-Za-z_][A-Za-z_0-9]*\s*\{\s*(?:public\s+)?(?:static\s+)?void\s+[A-Za-z_][A-Za-z_0-9]*\s*\(\s*\)\s*\{([^{}]*)\}\s*\}\s*$/d;
const statementShape = /^(?:;|(?:int\s+[A-Za-z_][A-Za-z_0-9]*\s*=\s*-?[0-9]+|[A-Za-z_][A-Za-z_0-9]*(?:\+\+|--)|[A-Za-z_][A-Za-z_0-9]*\s*=\s*-?[0-9]+|return)\s*;)$/;

export function projectStandIn(source: string, context: Context, revision: number): StandIn {
  const snapshot = context.snapshot;
  const span = (start: number, length: number): Span => ({ snapshot, coordinates: "utf16-offset", start, length });
  const graph: Graph = { context, graphId: "stand-in-body", revision, nodes: [], edges: [], regions: [],
    mappings: [], diagnostics: [], coverage: "unavailable" };
  const shape = methodShape.exec(source);
  const body = shape?.[1];
  const bodyStart = shape?.indices?.[1]?.[0];
  if (body === undefined || bodyStart === undefined) {
    return { editable: false, statements: [], graph: { ...graph, diagnostics: [{ code: "stand-in-shape", severity: "info",
      message: "This sample needs one class and one parameterless void method.", source: span(0, source.length) }] } };
  }
  const statements: Statement[] = [];
  const nodes: Graph["nodes"][number][] = [];
  const edges: Graph["edges"][number][] = [];
  const diagnostics: Graph["diagnostics"][number][] = [];
  let previous: string | undefined;
  let end = 0;
  for (const match of body.matchAll(/[^;]*;/g)) {
    const text = match[0].trim();
    const start = bodyStart + match.index + match[0].indexOf(text);
    end = match.index + match[0].length;
    if (!statementShape.test(text)) {
      diagnostics.push({ code: "stand-in-statement", severity: "info", message: "This statement is outside the sample grammar.", source: span(start, text.length) });
      previous = undefined;
      continue;
    }
    const id = `statement-${start}`;
    const terminal = /^return\s*;/.test(text);
    statements.push({ source: span(start, text.length), text, terminal });
    nodes.push({ id, kind: "sample-step", typeName: "" });
    if (previous !== undefined) edges.push({ id: `sequence-${start}`, from: previous, to: id, kind: "execution" });
    previous = terminal ? undefined : id;
  }
  if (body.slice(end).trim() !== "") diagnostics.push({ code: "stand-in-tail", severity: "info",
    message: "The remaining text is not a complete sample statement.", source: span(bodyStart + end, body.length - end) });
  const editable = diagnostics.length === 0;
  return { editable, statements, graph: { ...graph, nodes, edges,
    regions: [{ id: "method-body", kind: "body", nodeIds: nodes.map((node) => node.id) }],
    mappings: statements.map((statement, index) => ({ nodeId: nodes[index]!.id, source: statement.source })),
    diagnostics, coverage: editable ? "complete" : "partial" } };
}
