import { readInbound, writeOutbound } from "../../../src/surface/src/isolation/index.ts";
import { KeyboardSession } from "../../canvas/src/commands.ts";
import { describe, receiveGraph, sameSnapshot, type Graph } from "../../canvas/src/graph.ts";
import { schemas } from "./schemas.generated.ts";
import { commitRequest, graphRequest, insertionRequest, type Edit } from "./wire.ts";

export class SurfaceClient {
  readonly #send: (json: string) => void;
  readonly #prefix: string;
  #serial = 0;
  #session: KeyboardSession | undefined;
  #proposal: Edit | undefined;
  #preparing: string | undefined;
  #committing: string | undefined;
  #notice = "Waiting for the document graph.";
  constructor(prefix: string, send: (json: string) => void) { this.#prefix = prefix; this.#send = send; }
  get session(): KeyboardSession | undefined { return this.#session; }
  get proposal(): Edit | undefined { return this.#proposal; }
  get notice(): string { return this.#notice; }
  get busy(): boolean { return this.#preparing !== undefined || this.#committing !== undefined; }
  #id(): string { return `${this.#prefix}-${++this.#serial}`; }
  #post(message: unknown): void { this.#send(writeOutbound(message, schemas)); }
  start(): void { this.#post(graphRequest(this.#id())); }
  read(raw: unknown): boolean {
    const result = readInbound(raw, schemas);
    if (!result.accepted) { this.#notice = result.reason; return false; }
    if (result.family === "publish-graph") {
      const next = receiveGraph(raw as string, schemas, this.#session?.graph);
      if (!next.ok) { this.#notice = next.reason; return false; }
      const previous: Graph | undefined = this.#session?.graph;
      if (previous !== undefined && (next.graph.context.snapshot.documentUri !== previous.context.snapshot.documentUri ||
        Number(next.graph.context.snapshot.hostVersion) < Number(previous.context.snapshot.hostVersion))) return false;
      if (previous === undefined || !sameSnapshot(previous.context.snapshot, next.graph.context.snapshot)) {
        this.#proposal = undefined;
        this.#preparing = undefined;
      }
      if (this.#session === undefined) this.#session = new KeyboardSession(next.graph, schemas);
      else this.#session.synchronize(next.graph);
      this.#notice = this.#committing !== undefined ? "Document refreshed. The edit reply is still pending; inspect the source before continuing." :
        this.#preparing !== undefined ? "Waiting for the source preview." :
        next.graph.coverage === "complete" ? "Sample graph refreshed from the document." :
          "Only the small sample grammar is available; source editing is disabled here.";
      return true;
    }
    if (result.family !== "edit-result") return result.family === "initialize";
    const edit = result.message["payload"] as unknown as Edit;
    const requestId = result.message["requestId"] as string;
    if (edit.stage === "proposal") {
      if (requestId !== this.#preparing) return false;
      this.#preparing = undefined;
      if (edit.outcome === "proposed") {
        if (this.#session !== undefined && sameSnapshot(edit.context.snapshot, this.#session.graph.context.snapshot)) {
          this.#proposal = edit;
          this.#notice = "Review the source insertion, then choose Apply or Cancel.";
        } else {
          this.#proposal = undefined;
          this.#notice = "The preview is out of date. Request another preview.";
        }
        return true;
      }
    } else {
      if (requestId !== this.#committing) return false;
      this.#committing = undefined;
    }
    this.#proposal = undefined;
    this.#notice = edit.outcome === "rejected" ? edit.rejectionReason ?? "The edit was refused." : "Source updated. VS Code owns undo and redo.";
    return true;
  }
  prepare(): void {
    const session = this.#session;
    if (this.busy) return;
    if (session === undefined || session.graph.coverage !== "complete") { this.#notice = "Choose a supported sample statement first."; return; }
    const selected = session.inspect();
    const owner = selected?.kind === "node" ? selected : describe(session.graph).find((entry) => entry.key === selected?.owner);
    const anchor = session.graph.mappings.find((mapping) => mapping.nodeId === owner?.id)?.source;
    if (anchor === undefined) { this.#notice = "Focus a statement or one of its pins before inserting."; return; }
    this.#proposal = undefined;
    this.#preparing = this.#id();
    this.#post(insertionRequest(this.#preparing, session.graph.context, anchor));
    this.#notice = "Preparing the source preview.";
  }
  confirm(): void {
    const proposal = this.#proposal;
    if (proposal === undefined || this.busy) return;
    this.#proposal = undefined;
    this.#committing = this.#id();
    this.#post(commitRequest(this.#committing, proposal));
    this.#notice = "Applying the source edit. A lost reply must be checked in the document.";
  }
  cancel(): void {
    if (this.#committing !== undefined) { this.#notice = "The edit is already with VS Code; inspect the document before another edit."; return; }
    this.#preparing = undefined;
    this.#proposal = undefined;
    this.#notice = "Preview dismissed. No commit was sent.";
  }
}
