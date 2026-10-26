import { readInbound, type SchemaSet } from "../../../src/surface/src/isolation/index.ts";

export interface Snapshot {
  readonly hostId: string;
  readonly documentUri: string;
  readonly hostVersion: string;
  readonly snapshotId: string;
}
export interface Context {
  readonly workspaceId: string;
  readonly projectId: string;
  readonly compilationContextId: string;
  readonly snapshot: Snapshot;
}
export function sameSnapshot(a: Snapshot, b: Snapshot): boolean {
  return a.hostId === b.hostId && a.documentUri === b.documentUri && a.hostVersion === b.hostVersion && a.snapshotId === b.snapshotId;
}
export function sameContext(a: Context, b: Context): boolean {
  return a.workspaceId === b.workspaceId && a.projectId === b.projectId && a.compilationContextId === b.compilationContextId && sameSnapshot(a.snapshot, b.snapshot);
}
export interface Span {
  readonly snapshot: Snapshot;
  readonly coordinates: "utf16-offset";
  readonly start: number;
  readonly length: number;
}
export interface SourceNode {
  readonly id: string;
  readonly kind: string;
  readonly typeName: string;
}
export interface Edge {
  readonly id: string;
  readonly from: string;
  readonly to: string;
  readonly kind: string;
}
export interface Region {
  readonly id: string;
  readonly kind: string;
  readonly nodeIds: readonly string[];
}
export interface Diagnostic {
  readonly code: string;
  readonly severity: string;
  readonly message: string;
  readonly source: Span;
}
export interface Graph {
  readonly context: Context;
  readonly graphId: string;
  readonly revision: number;
  readonly nodes: readonly SourceNode[];
  readonly edges: readonly Edge[];
  readonly regions: readonly Region[];
  readonly mappings: readonly { readonly nodeId: string; readonly source: Span }[];
  readonly diagnostics: readonly Diagnostic[];
  readonly coverage: "complete" | "partial" | "unavailable";
}
interface Publication extends Graph {
  readonly mode: "complete" | "incremental";
  readonly baseRevision?: number;
  readonly removedNodeIds?: readonly string[];
  readonly removedEdgeIds?: readonly string[];
  readonly removedRegionIds?: readonly string[];
}

export type GraphRead = { readonly ok: true; readonly graph: Graph } | { readonly ok: false; readonly reason: string };

// Schema and input checks belong to SEC-04. This adapter only applies publications.
export function receiveGraph(raw: string, schemas: SchemaSet, previous?: Graph): GraphRead {
  const received = readInbound(raw, schemas);
  if (!received.accepted) return { ok: false, reason: received.reason };
  if (received.family !== "publish-graph") return { ok: false, reason: "No graph publication in this message." };
  const next = received.message["payload"] as unknown as Publication;
  if (next.mode === "complete") return { ok: true, graph: next };
  if (previous === undefined || previous.graphId !== next.graphId || previous.revision !== next.baseRevision ||
      previous.context.workspaceId !== next.context.workspaceId || previous.context.projectId !== next.context.projectId ||
      previous.context.compilationContextId !== next.context.compilationContextId ||
      previous.context.snapshot.hostId !== next.context.snapshot.hostId || previous.context.snapshot.documentUri !== next.context.snapshot.documentUri) {
    return { ok: false, reason: "A full graph is needed before this update can be displayed." };
  }
  const merge = <T extends { readonly id: string }>(old: readonly T[], changes: readonly T[], removed: readonly string[] = []): T[] => {
    const byId = new Map(old.filter((item) => !removed.includes(item.id)).map((item) => [item.id, item]));
    for (const item of changes) byId.set(item.id, item);
    return [...byId.values()];
  };
  return { ok: true, graph: {
    ...next,
    nodes: merge(previous.nodes, next.nodes, next.removedNodeIds),
    edges: merge(previous.edges, next.edges, next.removedEdgeIds),
    regions: merge(previous.regions, next.regions, next.removedRegionIds),
  } };
}

export type Role = "execution" | "value";
export interface Pin {
  readonly key: string;
  readonly nodeId: string;
  readonly name: string;
  readonly direction: "input" | "output";
  readonly side: "leading" | "trailing";
  readonly role: Role;
  readonly typeName: string;
  readonly passing: "value" | "none";
}
export interface PaletteItem {
  readonly kind: string;
  readonly title: string;
  readonly typeName: string;
  readonly inputs: readonly Role[];
  readonly outputs: readonly Role[];
}

// Deliberately small examples: protocol v0 does not transmit pin signatures.
export const palette: readonly PaletteItem[] = [
  { kind: "sample-number", title: "Number source", typeName: "System.Int32", inputs: [], outputs: ["value"] },
  { kind: "sample-text", title: "Text source", typeName: "System.String", inputs: [], outputs: ["value"] },
  { kind: "sample-display", title: "Number display", typeName: "System.Int32", inputs: ["execution", "value"], outputs: ["execution"] },
  { kind: "sample-step", title: "Sequence step", typeName: "", inputs: ["execution"], outputs: ["execution"] },
];

export type EntityKind = "node" | "pin" | "connection" | "region";
export function key(kind: EntityKind, id: string, pinName?: string): string {
  return JSON.stringify(pinName === undefined ? [kind, id] : [kind, id, pinName]);
}

export function pins(node: SourceNode): Pin[] {
  const definition = palette.find((item) => item.kind === node.kind);
  const groups = definition === undefined ? [] : [
    { direction: "input" as const, side: "leading" as const, roles: definition.inputs },
    { direction: "output" as const, side: "trailing" as const, roles: definition.outputs },
  ];
  return groups.flatMap((group) => group.roles.map((role) => {
    const name = `${role}-${group.direction}`;
    return { key: key("pin", node.id, name), nodeId: node.id, name, direction: group.direction,
      side: group.side, role, typeName: role === "value" ? node.typeName : "", passing: role === "value" ? "value" as const : "none" as const };
  }));
}

export function edgePins(graph: Graph, edge: Edge): { from?: Pin; to?: Pin } {
  const from = graph.nodes.find((node) => node.id === edge.from);
  const to = graph.nodes.find((node) => node.id === edge.to);
  return {
    from: from === undefined ? undefined : pins(from).find((pin) => pin.direction === "output" && pin.role === edge.kind),
    to: to === undefined ? undefined : pins(to).find((pin) => pin.direction === "input" && pin.role === edge.kind),
  };
}

export function connectionsAt(graph: Graph, pin: Pin): Edge[] {
  return graph.edges.filter((edge) => edge.kind === pin.role && (pin.direction === "input" ? edge.to : edge.from) === pin.nodeId);
}

export function regionFor(graph: Graph, nodeId: string): Region | undefined {
  return graph.regions.filter((region) => region.nodeIds.includes(nodeId))
    .sort((a, b) => a.nodeIds.length - b.nodeIds.length)[0];
}

export interface Entry {
  readonly key: string;
  readonly id: string;
  readonly kind: EntityKind;
  readonly owner?: string;
  readonly parts: readonly string[];
  readonly actions: readonly string[];
  readonly from?: string;
  readonly to?: string;
  readonly members?: readonly string[];
}

export function describe(graph: Graph): Entry[] {
  const entries: Entry[] = [];
  for (const node of graph.nodes) {
    const spans = graph.mappings.filter((mapping) => mapping.nodeId === node.id).map((mapping) => mapping.source);
    const diagnostics = graph.diagnostics.filter((diagnostic) => spans.some((span) =>
      sameSnapshot(span.snapshot, diagnostic.source.snapshot) &&
      span.start <= diagnostic.source.start + diagnostic.source.length && diagnostic.source.start <= span.start + span.length));
    const nodePins = pins(node);
    entries.push({ key: key("node", node.id), id: node.id, kind: "node",
      parts: [node.kind, node.id, node.typeName || "No value type", ...diagnostics.map((d) => `${d.severity}: ${d.code}: ${d.message}`)],
      actions: nodePins.length > 0 ? ["Inspect", "Choose pin", "Insert nearby"] : ["Inspect"] });
    for (const pin of nodePins) {
      const count = connectionsAt(graph, pin).length;
      entries.push({ key: pin.key, id: pin.name, kind: "pin", owner: key("node", node.id),
        parts: [pin.name, pin.direction, pin.role, pin.typeName || "No value type", `Passing: ${pin.passing}`, count === 0 ? "Unconnected" : `${count} connection(s)`],
        actions: count > 0 ? ["Inspect", "Connect", ...(pin.role === "value" ? ["Disconnect with replacement"] : []), "Reconnect"] : ["Inspect", "Connect"] });
    }
  }
  for (const edge of graph.edges) {
    const endpoints = edgePins(graph, edge);
    entries.push({ key: key("connection", edge.id), id: edge.id, kind: "connection", owner: key("node", edge.from),
      parts: [edge.kind, edge.id, `From ${edge.from}`, `To ${edge.to}`],
      actions: ["Inspect", ...(edge.kind === "value" ? ["Disconnect with replacement"] : []), ...(endpoints.from === undefined ? [] : ["Reconnect"])],
      from: endpoints.from?.key ?? key("node", edge.from), to: endpoints.to?.key ?? key("node", edge.to) });
  }
  for (const region of graph.regions) {
    entries.push({ key: key("region", region.id), id: region.id, kind: "region", parts: [region.kind, region.id],
      actions: ["Inspect", "Enter region", "Expand or collapse"], members: region.nodeIds.map((id) => key("node", id)) });
  }
  return entries;
}
