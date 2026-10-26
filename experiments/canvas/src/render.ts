import { element, label, text, type Attributes, type DocumentLike, type ElementLike, type NodeLike } from "../../../src/surface/src/isolation/index.ts";
import { describe, key, pins, type Entry, type Graph } from "./graph.ts";
import { layout, mapBox, mapPoint, pinPosition, size, type Point, type Position } from "./layout.ts";

export interface ViewState {
  readonly focus?: string;
  readonly selected?: ReadonlySet<string>;
  readonly collapsed?: ReadonlySet<string>;
  readonly proposals?: readonly Entry[];
  readonly announcement?: string;
}
export interface Views {
  readonly canvas: ElementLike;
  readonly outline: ElementLike;
}

// The supported presentation is LTR. Other mappings are exercised separately.
export function render(document: DocumentLike, graph: Graph, state: ViewState = {}, instance = "graph"): Views {
  const entriesByKey = new Map(describe(graph).map((entry) => [entry.key, entry]));
  for (const proposal of state.proposals ?? []) entriesByKey.set(proposal.key, { ...entriesByKey.get(proposal.key), ...proposal });
  const entries = [...entriesByKey.values()];
  const ordinal = new Map(entries.map((entry, index) => [entry.key, index]));
  const placement = layout(graph);
  const positions = new Map(placement.positions);
  let extent = placement.extent;
  for (const entry of entries) {
    if (entry.kind === "node" && !positions.has(entry.id)) {
      positions.set(entry.id, { along: 40, across: extent.across + 40 });
      extent = { along: extent.along, across: extent.across + 192 };
    }
  }
  const points = new Map<string, Position>();
  for (const [id, position] of positions) points.set(key("node", id), { along: position.along + size.along / 2, across: position.across + size.across / 2 });
  for (const node of graph.nodes) for (const pin of pins(node)) points.set(pin.key, pinPosition(graph, placement, pin));
  const physical = (position: Position) => mapPoint(position, extent, "left-to-right");
  const boxAttributes = (position: Position, dimensions: Position): Attributes => {
    const box = mapBox(position, dimensions, extent, "left-to-right");
    return { x: String(box.x), y: String(box.y), width: String(box.width), height: String(box.height) };
  };
  const labelId = (view: string, entry: Entry) => `${instance}-${view}-label-${ordinal.get(entry.key)}`;
  const identity = (entry: Entry): Attributes => ({
    "data-ws-key": entry.key, "data-ws-kind": entry.kind, "data-ws-id": entry.id,
    ...(entry.owner === undefined ? {} : { "data-ws-owner": entry.owner }),
    ...(entry.from === undefined ? {} : { "data-ws-from": entry.from }),
    ...(entry.to === undefined ? {} : { "data-ws-to": entry.to }),
  });
  const caption = (entry: Entry, view: string): ElementLike => {
    const parts = [entry.kind, ...entry.parts, state.selected?.has(entry.key) ? "Selected" : "Not selected", `Actions: ${entry.actions.join(", ")}`];
    if (state.focus === entry.key) parts.push("Keyboard focus");
    return element(document, "div", { id: labelId(view, entry), class: "entry-caption" },
      parts.flatMap((part, index) => index === 0 ? [label(document, part)] : [text(document, " · "), label(document, part)]));
  };
  const memberLabels = (entry: Entry, tree: boolean): ElementLike | undefined => {
    if (entry.members === undefined || entry.members.length === 0) return undefined;
    return element(document, "ul", { ...(tree ? { role: "group" } : {}),
      ...(tree && state.collapsed?.has(entry.key) ? { hidden: "hidden" } : {}) }, entry.members.map((member) => {
      const node = entriesByKey.get(member);
      return element(document, "li", { "data-ws-reference": member,
        ...(tree ? { role: "treeitem", "aria-level": "2", "aria-selected": String(state.selected?.has(member) ?? false) } : {}) },
      [label(document, node === undefined ? "Unavailable member" : `${node.parts[0]}: ${node.id}`)]);
    }));
  };

  const connectionShape = (entry: Entry): NodeLike[] => {
    const from = points.get(entry.from ?? "");
    const to = points.get(entry.to ?? "");
    if (from === undefined || to === undefined) return [];
    const a = physical(from);
    const b = physical(to);
    const execution = entry.parts.includes("execution");
    const midpoint = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    const vector = { x: b.x - a.x, y: b.y - a.y };
    const length = Math.hypot(vector.x, vector.y) || 1;
    const unit = { x: vector.x / length, y: vector.y / length };
    const tip = (offset: number): Point => ({ x: b.x - unit.x * 10 - unit.y * offset, y: b.y - unit.y * 10 + unit.x * offset });
    const rounded = (number: number) => String(Math.round(number * 100) / 100);
    const pair = (point: Point) => `${rounded(point.x)},${rounded(point.y)}`;
    const line = execution ? `M ${pair(a)} L ${pair(b)}` :
      `M ${pair(a)} L ${pair({ x: midpoint.x - unit.x * 5, y: midpoint.y - unit.y * 5 })} M ${pair({ x: midpoint.x + unit.x * 5, y: midpoint.y + unit.y * 5 })} L ${pair(b)}`;
    return [element(document, "path", { class: execution ? "connection-execution" : "connection-value", d: line }),
      element(document, "polyline", { class: "connection-arrow", points: `${pair(tip(-5))} ${pair(b)} ${pair(tip(5))}` })];
  };

  const glyph = (entry: Entry): ElementLike => {
    let shapes: NodeLike[] = [];
    if (entry.kind === "node") {
      const position = positions.get(entry.id);
      if (position !== undefined) shapes = [element(document, "rect", { class: "node-box", ...boxAttributes(position, size), rx: "8" })];
    } else if (entry.kind === "pin") {
      const point = points.get(entry.key);
      if (point !== undefined) {
        const { x, y } = physical(point);
        shapes = [entry.parts.includes("execution") ?
          element(document, "path", { class: "pin-execution", d: `M ${x} ${y - 6} L ${x + 6} ${y} L ${x} ${y + 6} L ${x - 6} ${y} Z` }) :
          element(document, "circle", { class: "pin-value", cx: String(x), cy: String(y), r: "5" })];
      }
    } else if (entry.kind === "connection") {
      shapes = connectionShape(entry);
    } else {
      const region = graph.regions.find((item) => item.id === entry.id);
      const members = region?.nodeIds.flatMap((id) => { const p = positions.get(id); return p === undefined ? [] : [p]; }) ?? [];
      if (members.length > 0) {
        const start = { along: Math.min(...members.map((p) => p.along)) - 16, across: Math.min(...members.map((p) => p.across)) - 16 };
        const dimensions = { along: Math.max(...members.map((p) => p.along)) + size.along + 16 - start.along,
          across: Math.max(...members.map((p) => p.across)) + size.across + 16 - start.across };
        shapes = [element(document, "rect", { class: "region-box", ...boxAttributes(start, dimensions), rx: "12" })];
      }
    }
    return element(document, "g", { ...identity(entry), role: "group", "aria-labelledby": labelId("canvas", entry),
      tabindex: state.focus === entry.key ? "0" : "-1", class: state.focus === entry.key ? "graph-item focused" : "graph-item" }, shapes);
  };

  const layers = ["region", "connection", "node", "pin"] as const;
  const svg = element(document, "svg", { class: "graph-drawing", role: "group", "aria-label": "Graph geometry",
    viewBox: `0 0 ${extent.along} ${extent.across}`, width: String(extent.along), height: String(extent.across) },
  layers.flatMap((kind) => entries.filter((entry) => entry.kind === kind).map(glyph)));
  const canvasCaptions = element(document, "ol", { class: "canvas-captions", "aria-label": "Diagram labels" }, entries.map((entry) => {
    const members = memberLabels(entry, false);
    return element(document, "li", { "data-ws-label-for": entry.key }, [caption(entry, "canvas"), ...(members === undefined ? [] : [members])]);
  }));
  const canvas = element(document, "div", { class: "ws-canvas", role: "group", "aria-label": "Graph and labels" }, [svg, canvasCaptions]);

  const children = new Map<string, Entry[]>();
  for (const entry of entries) if (entry.owner !== undefined && entriesByKey.has(entry.owner)) {
    const list = children.get(entry.owner) ?? [];
    list.push(entry);
    children.set(entry.owner, list);
  }
  const treeItem = (entry: Entry, level: number): ElementLike => {
    const nested = children.get(entry.key) ?? [];
    const members = memberLabels(entry, true);
    const hasChildren = nested.length > 0 || members !== undefined;
    return element(document, "li", { ...identity(entry), role: "treeitem", "aria-level": String(level),
      "aria-selected": String(state.selected?.has(entry.key) ?? false), "aria-labelledby": labelId("outline", entry),
      tabindex: state.focus === entry.key ? "0" : "-1",
      ...(hasChildren ? { "aria-expanded": String(!state.collapsed?.has(entry.key)) } : {}) },
    [caption(entry, "outline"), ...(nested.length === 0 ? [] : [element(document, "ul", { role: "group" }, nested.map((child) => treeItem(child, level + 1)))]),
      ...(members === undefined ? [] : [members])]);
  };
  const tree = element(document, "ul", { role: "tree", "aria-label": "Graph outline", tabindex: state.focus === undefined ? "0" : "-1" },
    entries.filter((entry) => entry.owner === undefined || !entriesByKey.has(entry.owner)).map((entry) => treeItem(entry, 1)));
  const status = element(document, "p", { role: "status", "aria-live": "polite", "aria-atomic": "true" }, [label(document, state.announcement ?? "Graph ready.")]);
  const outline = element(document, "div", { class: "ws-outline" }, [tree, status]);
  return { canvas, outline };
}
