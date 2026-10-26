import { IsolationError, writeOutbound, type SchemaSet } from "../../../src/surface/src/isolation/index.ts";
import { connectionsAt, describe, edgePins, key, palette, pins, regionFor, sameContext, sameSnapshot, type Edge, type Entry, type Graph, type Pin, type Span } from "./graph.ts";
import type { LogicalMove } from "./layout.ts";

export interface CommandResult {
  readonly ok: boolean;
  readonly message: string;
  readonly outbound?: string;
}
export interface Destination {
  readonly pin: Pin;
  readonly allowed: boolean;
  readonly reason: string;
}
type Draft = {
  readonly origin: string | undefined;
  readonly revision: number;
  readonly id: string;
  submitted: boolean;
} & (
  | { readonly kind: "connect"; readonly start: Pin; target?: Pin; readonly replacing?: string }
  | { readonly kind: "insert"; readonly paletteKind: string; readonly anchor: Span }
  | { readonly kind: "disconnect"; readonly edge: Edge; readonly replacement: string }
);

export class KeyboardSession {
  #graph: Graph;
  readonly #schemas: SchemaSet;
  #focus: string | undefined;
  #draft: Draft | undefined;
  #serial = 0;
  readonly #selected = new Set<string>();
  readonly #collapsed = new Set<string>();
  #announcement = "Graph ready.";

  constructor(graph: Graph, schemas: SchemaSet) {
    this.#graph = graph;
    this.#schemas = schemas;
    this.#focus = graph.nodes[0] === undefined ? undefined : key("node", graph.nodes[0].id);
  }

  get graph(): Graph { return this.#graph; }
  get focus(): string | undefined { return this.#focus; }
  get announcement(): string { return this.#announcement; }
  get selected(): ReadonlySet<string> { return new Set(this.#selected); }
  get collapsed(): ReadonlySet<string> { return new Set(this.#collapsed); }

  #result(ok: boolean, message: string, outbound?: string): CommandResult {
    this.#announcement = message;
    return { ok, message, ...(outbound === undefined ? {} : { outbound }) };
  }

  #nextId(kind: "node" | "connection"): string {
    let id: string;
    do { id = `draft-${kind}-${++this.#serial}`; } while (describe(this.#graph).some((entry) => entry.key === key(kind, id)));
    return id;
  }

  #findPin(pinKey: string | undefined): Pin | undefined {
    return this.#graph.nodes.flatMap(pins).find((pin) => pin.key === pinKey);
  }

  #nodeId(entityKey: string | undefined): string | undefined {
    const entry = describe(this.#graph).find((item) => item.key === entityKey);
    if (entry?.kind === "node") return entry.id;
    if (entry?.kind === "pin") return this.#findPin(entry.key)?.nodeId;
    if (entry?.kind === "connection") return this.#graph.edges.find((edge) => edge.id === entry.id)?.from;
    return undefined;
  }

  focusEntity(entityKey: string): CommandResult {
    if (![...describe(this.#graph), ...this.proposals()].some((entry) => entry.key === entityKey)) {
      return this.#result(false, "That item is no longer present.");
    }
    this.#focus = entityKey;
    return this.#result(true, "Focus moved.");
  }

  focusNode(id: string): CommandResult { return this.focusEntity(key("node", id)); }

  move(command: LogicalMove): CommandResult {
    const nodeId = this.#nodeId(this.#focus);
    const node = this.#graph.nodes.find((item) => item.id === nodeId);
    if (command === "next" || command === "previous") {
      const index = node === undefined ? -1 : this.#graph.nodes.indexOf(node);
      const next = this.#graph.nodes[index + (command === "next" ? 1 : -1)];
      return next === undefined ? this.#result(false, "There is no further node in that direction.") : this.focusNode(next.id);
    }
    if (node === undefined) return this.#result(false, "Choose a node to browse its pins.");
    const available = pins(node);
    const index = available.findIndex((pin) => pin.key === this.#focus);
    const next = available[index < 0 ? (command === "across-next" ? 0 : available.length - 1) : index + (command === "across-next" ? 1 : -1)];
    return next === undefined ? this.#result(false, "There is no further pin on this node.") : this.focusEntity(next.key);
  }

  inspect(): Entry | undefined {
    return [...this.proposals(), ...describe(this.#graph)].find((entry) => entry.key === this.#focus);
  }

  searchPalette(query = "") {
    const search = query.toLowerCase();
    return palette.filter((item) => `${item.title} ${item.typeName}`.toLowerCase().includes(search));
  }

  beginConnect(): CommandResult {
    if (this.#draft !== undefined) return this.#result(false, "Finish or cancel the current proposal first.");
    const start = this.#findPin(this.#focus);
    if (start === undefined) return this.#result(false, "Connect starts at a named pin.");
    this.#draft = { kind: "connect", start, origin: this.#focus, id: this.#nextId("connection"), revision: this.#graph.revision, submitted: false };
    return this.#result(true, "Choose a destination, then inspect the proposal.");
  }

  beginReconnect(edgeId: string): CommandResult {
    if (this.#draft !== undefined) return this.#result(false, "Finish or cancel the current proposal first.");
    const edge = this.#graph.edges.find((item) => item.id === edgeId);
    const start = edge === undefined ? undefined : edgePins(this.#graph, edge).from;
    if (start === undefined) return this.#result(false, "This relationship has no editable output pin in the sample catalogue.");
    this.#draft = { kind: "connect", start, replacing: edgeId, origin: key("connection", edgeId),
      id: this.#nextId("connection"), revision: this.#graph.revision, submitted: false };
    return this.#result(true, "Choose the new destination; the existing relationship remains until the host applies an edit.");
  }

  #compatibility(start: Pin, target: Pin, replacing?: string): string {
    if (this.#graph.coverage !== "complete") return "This sample needs complete graph information to propose an edit.";
    if (start.nodeId === target.nodeId) return "A node cannot connect to itself in this sample.";
    if (start.direction === target.direction) return "A connection needs an input and an output.";
    if (start.role !== target.role) return "Execution and value pins serve different purposes.";
    if (start.role === "value" && (!start.typeName || !target.typeName)) return "The value type is unavailable.";
    if (start.role === "value" && start.typeName !== target.typeName) return "These value types differ; no conversion is offered by this sample.";
    if (regionFor(this.#graph, start.nodeId)?.id !== regionFor(this.#graph, target.nodeId)?.id) return "This proposal would cross a region boundary.";
    const input = start.direction === "input" ? start : target;
    if (connectionsAt(this.#graph, input).some((edge) => edge.id !== replacing)) return "The input already has a relationship; use Reconnect.";
    return "";
  }

  destinations(query = ""): Destination[] {
    const draft = this.#draft;
    if (draft?.kind !== "connect" || draft.submitted) return [];
    const search = query.toLowerCase();
    return this.#graph.nodes.flatMap(pins)
      .filter((pin) => `${pin.nodeId} ${pin.name} ${pin.typeName}`.toLowerCase().includes(search))
      .map((pin) => {
        const reason = this.#compatibility(draft.start, pin, draft.replacing);
        return { pin, allowed: reason === "", reason: reason || "Available for a host preview." };
      });
  }

  chooseDestination(pinKey: string): CommandResult {
    const draft = this.#draft;
    const candidate = this.destinations().find((item) => item.pin.key === pinKey);
    if (draft?.kind !== "connect" || candidate === undefined) return this.#result(false, "Start a connection and choose a listed pin.");
    if (!candidate.allowed) return this.#result(false, candidate.reason);
    draft.target = candidate.pin;
    this.#focus = key("connection", draft.id);
    return this.#result(true, "Proposal selected. Inspect it before requesting the host preview.");
  }

  beginInsert(paletteKind: string, site?: Span): CommandResult {
    if (this.#draft !== undefined) return this.#result(false, "Finish or cancel the current proposal first.");
    const item = palette.find((entry) => entry.kind === paletteKind);
    if (item === undefined) return this.#result(false, "That operation is not in the sample palette.");
    const nodeId = this.#nodeId(this.#focus);
    const anchor = site ?? this.#graph.mappings.find((mapping) => mapping.nodeId === nodeId)?.source;
    if (anchor === undefined) return this.#result(false, "Choose a source-owned insertion position.");
    this.#draft = { kind: "insert", paletteKind, anchor, origin: this.#focus,
      id: this.#nextId("node"), revision: this.#graph.revision, submitted: false };
    this.#focus = key("node", this.#draft.id);
    return this.#result(true, "Insertion proposed. The host must supply the source preview.");
  }

  beginDisconnect(edgeId: string, replacement: string): CommandResult {
    if (this.#draft !== undefined) return this.#result(false, "Finish or cancel the current proposal first.");
    const edge = this.#graph.edges.find((item) => item.id === edgeId);
    if (edge === undefined) return this.#result(false, "That relationship is no longer present.");
    if (edge.kind !== "value") return this.#result(false, "A control relationship needs a structured control edit.");
    if (replacement.trim() === "") return this.#result(false, "Choose the replacement expression before disconnecting this input.");
    this.#draft = { kind: "disconnect", edge, replacement, origin: key("connection", edge.id),
      id: edge.id, revision: this.#graph.revision, submitted: false };
    this.#focus = key("connection", edge.id);
    return this.#result(true, "Replacement proposed; nothing has been removed from the source.");
  }

  proposals(): Entry[] {
    const draft = this.#draft;
    if (draft === undefined) return [];
    const status = draft.submitted ? "Awaiting host preview" : "Proposed edit";
    if (draft.kind === "insert") return [{ key: key("node", draft.id), id: draft.id, kind: "node",
      parts: [status, draft.paletteKind, "Insert at the chosen source position"], actions: ["Inspect", "Cancel"] }];
    if (draft.kind === "disconnect") return [{ key: key("connection", draft.id), id: draft.id, kind: "connection", owner: key("node", draft.edge.from),
      parts: [status, "Disconnect value", `Replacement: ${draft.replacement}`], actions: ["Inspect", "Cancel"] }];
    if (draft.target === undefined) return [];
    const output = draft.start.direction === "output" ? draft.start : draft.target;
    const input = draft.start.direction === "input" ? draft.start : draft.target;
    return [{ key: key("connection", draft.id), id: draft.id, kind: "connection", owner: key("node", output.nodeId),
      from: output.key, to: input.key, parts: [status, output.role, `From ${output.nodeId}: ${output.name}`, `To ${input.nodeId}: ${input.name}`,
        output.role === "value" ? "Evaluate at the chosen use; no stored result is introduced here" : "Request an explicit sequencing preview"], actions: ["Inspect", "Cancel"] }];
  }

  commit(): CommandResult {
    const draft = this.#draft;
    if (draft === undefined) return this.#result(false, "There is no proposed edit to prepare.");
    if (draft.submitted) return this.#result(false, "The host preview is already requested.");
    if (draft.revision !== this.#graph.revision) return this.#result(false, "The graph changed; start a fresh proposal.");
    let command: string;
    let anchors: Span[];
    const choices: { name: string; value: string }[] = [
      { name: "graph-id", value: this.#graph.graphId }, { name: "graph-revision", value: String(this.#graph.revision) },
      { name: "requested-id", value: draft.id },
    ];
    if (draft.kind === "insert") {
      command = "insert-operation";
      anchors = [draft.anchor];
      choices.push({ name: "operation", value: draft.paletteKind });
    } else {
      if (draft.kind === "connect") {
        if (draft.target === undefined) return this.#result(false, "Choose a destination before committing.");
        const reason = this.#compatibility(draft.start, draft.target, draft.replacing);
        if (reason !== "") return this.#result(false, reason);
        const output = draft.start.direction === "output" ? draft.start : draft.target;
        const input = draft.start.direction === "input" ? draft.start : draft.target;
        command = output.role === "value" ? "connect-value" : "connect-execution";
        choices.push({ name: "from-node", value: output.nodeId }, { name: "from-pin", value: output.name },
          { name: "to-node", value: input.nodeId }, { name: "to-pin", value: input.name },
          { name: "source-form", value: output.role === "value" ? "expression-at-use" : "sequence" });
        if (draft.replacing !== undefined) choices.push({ name: "replace-relationship", value: draft.replacing });
        anchors = [output.nodeId, input.nodeId].flatMap((id) => this.#graph.mappings.filter((mapping) => mapping.nodeId === id).map((mapping) => mapping.source));
        if (![output.nodeId, input.nodeId].every((id) => this.#graph.mappings.some((mapping) => mapping.nodeId === id))) {
          return this.#result(false, "Both endpoints need source locations for a host preview.");
        }
      } else {
        command = "disconnect-value";
        anchors = this.#graph.mappings.filter((mapping) => mapping.nodeId === draft.edge.to).map((mapping) => mapping.source);
        choices.push({ name: "relationship", value: draft.edge.id }, { name: "replacement-expression", value: draft.replacement });
      }
    }
    if (anchors.length === 0) return this.#result(false, "The edit has no source location.");
    if (anchors.some((anchor) => !sameSnapshot(anchor.snapshot, this.#graph.context.snapshot))) {
      return this.#result(false, "The insertion or connection location belongs to an older source snapshot.");
    }
    let outbound: string;
    try {
      outbound = writeOutbound({ version: { major: 0, minor: 0 }, family: "prepare-edit", kind: "query", requestId: `canvas-${++this.#serial}`,
        payload: { context: this.#graph.context, command, anchors, choices, destinations: [this.#graph.context.snapshot] }, extensions: {} }, this.#schemas);
    } catch (error) {
      if (!(error instanceof IsolationError)) throw error;
      return this.#result(false, `The preview request could not be prepared: ${error.message}`);
    }
    draft.submitted = true;
    if (draft.kind === "disconnect") {
      this.#focus = edgePins(this.#graph, draft.edge).to?.key ?? key("node", draft.edge.to);
    } else {
      this.#focus = key(draft.kind === "insert" ? "node" : "connection", draft.id);
    }
    return this.#result(true, "Host preview requested. Source and published graph are unchanged.", outbound);
  }

  cancel(): CommandResult {
    if (this.#draft === undefined) return this.#result(false, "There is no proposal to cancel.");
    this.#focus = this.#draft.origin;
    this.#draft = undefined;
    return this.#result(true, "Proposal canceled; focus returned to its starting point.");
  }

  rejectPending(reason: string): CommandResult {
    if (this.#draft === undefined) return this.#result(false, "There is no pending proposal.");
    this.#focus = this.#draft.origin;
    this.#draft = undefined;
    return this.#result(false, `Host refused the proposal: ${reason}`);
  }

  enterRegion(id: string): CommandResult {
    const region = this.#graph.regions.find((item) => item.id === id);
    if (region === undefined) return this.#result(false, "That region is no longer present.");
    this.#collapsed.delete(key("region", id));
    const node = this.#graph.nodes.find((item) => region.nodeIds.includes(item.id));
    return this.focusEntity(node === undefined ? key("region", id) : key("node", node.id));
  }

  toggleRegion(id: string): CommandResult {
    const regionKey = key("region", id);
    if (!this.#graph.regions.some((item) => item.id === id)) return this.#result(false, "That region is no longer present.");
    if (this.#collapsed.has(regionKey)) this.#collapsed.delete(regionKey);
    else this.#collapsed.add(regionKey);
    return this.#result(true, "Region membership display changed; keyboard focus was retained.");
  }

  expandSelection(): CommandResult {
    const nodeId = this.#nodeId(this.#focus);
    const region = nodeId === undefined ? undefined : regionFor(this.#graph, nodeId);
    if (region === undefined) return this.#result(false, "There is no containing region to select.");
    for (const id of region.nodeIds) this.#selected.add(key("node", id));
    return this.#result(true, "Selected the region's nodes; keyboard focus stayed in place.");
  }

  synchronize(next: Graph): CommandResult {
    const old = this.#graph;
    const oldFocus = this.#focus;
    const oldNodeId = this.#nodeId(oldFocus);
    const draft = this.#draft;
    const originNodeId = this.#nodeId(draft?.origin);
    const oldRegion = old.regions.find((region) => key("region", region.id) === oldFocus);
    const available = new Set(describe(next).map((entry) => entry.key));
    this.#graph = next;
    for (const selected of this.#selected) if (!available.has(selected)) this.#selected.delete(selected);
    for (const collapsed of this.#collapsed) if (!available.has(collapsed)) this.#collapsed.delete(collapsed);
    const changed = old.graphId !== next.graphId || old.revision !== next.revision || !sameContext(old.context, next.context);
    if (!changed) return this.#result(true, "Graph refreshed; focus retained.");
    if (old.graphId !== next.graphId || old.context.snapshot.documentUri !== next.context.snapshot.documentUri) {
      this.#draft = undefined;
      this.#focus = undefined;
      this.#selected.clear();
      this.#collapsed.clear();
      return this.#result(true, "A different graph opened. Focus moved to the graph root.");
    }
    if (draft !== undefined) {
      this.#draft = undefined;
      const requestedEffectPresent = draft.kind === "insert" ? next.nodes.some((node) => node.id === draft.id && node.kind === draft.paletteKind) :
        draft.kind === "disconnect" ? !next.edges.some((edge) => edge.id === draft.edge.id) :
          draft.target !== undefined && next.edges.some((edge) => edge.id === draft.id && edge.kind === draft.start.role &&
            edge.from === (draft.start.direction === "output" ? draft.start.nodeId : draft.target!.nodeId) &&
            edge.to === (draft.start.direction === "input" ? draft.start.nodeId : draft.target!.nodeId)) &&
            (draft.replacing === undefined || !next.edges.some((edge) => edge.id === draft.replacing));
      if (draft.submitted && requestedEffectPresent && old.graphId === next.graphId && this.#focus !== undefined && available.has(this.#focus)) {
        return this.#result(true, "Published graph received; focus retained on the affected item.");
      }
      this.#focus = draft.origin;
    }
    if (this.#focus !== undefined && available.has(this.#focus)) {
      return this.#result(true, draft === undefined ? "Source refreshed; focus retained." : "Source changed; the proposal was cleared. Review the new source before retrying.");
    }
    const nodeId = oldNodeId ?? originNodeId ?? (draft?.kind === "connect" ? draft.start.nodeId : undefined);
    const surviving = old.regions.filter((region) => available.has(key("region", region.id)) &&
      (nodeId !== undefined ? region.nodeIds.includes(nodeId) : oldRegion !== undefined && oldRegion.nodeIds.length > 0 &&
        region.id !== oldRegion.id && oldRegion.nodeIds.every((id) => region.nodeIds.includes(id))))
      .sort((a, b) => a.nodeIds.length - b.nodeIds.length)[0];
    this.#focus = surviving === undefined ? undefined : key("region", surviving.id);
    return this.#result(true, surviving === undefined ? "The focused item was removed. Focus moved to the graph root." : "The focused item was removed. Focus moved to its surviving region.");
  }
}
