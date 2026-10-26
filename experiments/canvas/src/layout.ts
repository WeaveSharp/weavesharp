import { pins, type Graph, type Pin } from "./graph.ts";

export type Flow = "left-to-right" | "top-to-bottom" | "right-to-left";
export type LogicalMove = "next" | "previous" | "across-next" | "across-previous";
export interface Position { readonly along: number; readonly across: number }
export interface Point { readonly x: number; readonly y: number }
export interface Layout {
  readonly positions: ReadonlyMap<string, Position>;
  readonly extent: Position;
}
export const size = { along: 160, across: 112 } as const;

// Bounded relaxation on source-forward edges keeps loops from creating unbounded ranks.
// Backward edges keep their direction; they do not reverse source order for placement.
export function layout(graph: Graph): Layout {
  const order = new Map(graph.nodes.map((node, index) => [node.id, index]));
  const incoming = new Map<string, string[]>();
  for (const edge of graph.edges) {
    if ((order.get(edge.from) ?? Infinity) >= (order.get(edge.to) ?? -1)) continue;
    const sources = incoming.get(edge.to) ?? [];
    sources.push(edge.from);
    incoming.set(edge.to, sources);
  }
  const ranks = new Map<string, number>();
  const rows = new Map<number, number>();
  const positions = new Map<string, Position>();
  let along = 240;
  let across = 192;
  for (const node of graph.nodes) {
    const rank = Math.max(0, ...(incoming.get(node.id) ?? []).map((id) => (ranks.get(id) ?? 0) + 1));
    const row = rows.get(rank) ?? 0;
    ranks.set(node.id, rank);
    rows.set(rank, row + 1);
    const position = { along: 40 + rank * 240, across: 40 + row * 176 };
    positions.set(node.id, position);
    along = Math.max(along, position.along + size.along + 40);
    across = Math.max(across, position.across + size.across + 40);
  }
  return { positions, extent: { along, across } };
}

export function mapPoint(position: Position, extent: Position, flow: Flow): Point {
  switch (flow) {
    case "left-to-right": return { x: position.along, y: position.across };
    case "right-to-left": return { x: extent.along - position.along, y: position.across };
    case "top-to-bottom": return { x: position.across, y: position.along };
  }
}

export function mapBox(position: Position, dimensions: Position, extent: Position, flow: Flow): Point & { width: number; height: number } {
  const end = mapPoint({ along: position.along + dimensions.along, across: position.across + dimensions.across }, extent, flow);
  const start = mapPoint(position, extent, flow);
  return { x: Math.min(start.x, end.x), y: Math.min(start.y, end.y), width: Math.abs(end.x - start.x), height: Math.abs(end.y - start.y) };
}

export function pinPosition(graph: Graph, placement: Layout, pin: Pin): Position {
  const node = graph.nodes.find((item) => item.id === pin.nodeId);
  const position = placement.positions.get(pin.nodeId);
  if (node === undefined || position === undefined) throw new Error("The pin has no placed node.");
  const sidePins = pins(node).filter((item) => item.side === pin.side);
  return { along: position.along + (pin.side === "leading" ? 0 : size.along),
    across: position.across + Math.round((sidePins.findIndex((item) => item.key === pin.key) + 1) * size.across / (sidePins.length + 1)) };
}

export function arrowCommand(arrow: string, flow: Flow): LogicalMove | undefined {
  const bindings: Record<Flow, Readonly<Record<string, LogicalMove>>> = {
    "left-to-right": { ArrowRight: "next", ArrowLeft: "previous", ArrowDown: "across-next", ArrowUp: "across-previous" },
    "right-to-left": { ArrowLeft: "next", ArrowRight: "previous", ArrowDown: "across-next", ArrowUp: "across-previous" },
    "top-to-bottom": { ArrowDown: "next", ArrowUp: "previous", ArrowRight: "across-next", ArrowLeft: "across-previous" },
  };
  return bindings[flow][arrow];
}
