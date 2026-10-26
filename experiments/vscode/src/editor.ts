import { readFromSurface, writeToSurface, IsolationError } from "../../../src/surface/src/isolation/index.ts";
import { sameContext, type Context } from "../../canvas/src/graph.ts";
import { schemas } from "./schemas.generated.ts";
import { editProblem, matchesProposal, planInsertion, snapshotProblem } from "./edits.ts";
import { projectStandIn } from "./stand-in.ts";
import { envelope, openingContext, type Change, type Edit, type Preparation } from "./wire.ts";

export interface OwnedDocument {
  readonly uri: string;
  readonly version: number;
  getText(): string;
  apply(version: number, changes: readonly Change[]): PromiseLike<"applied" | "stale" | "failed">;
}
export interface Handling { readonly accepted: boolean; readonly reason?: string; readonly outcome?: string }

export class GraphEditor {
  readonly #document: OwnedDocument;
  readonly #post: (json: string) => PromiseLike<boolean>;
  readonly #identity: string;
  readonly #requests = new Set<string>();
  #serial = 0;
  #proposal: Edit | undefined;
  #applying = false;
  #disposed = false;
  #initialized = false;
  #lastPublication: string | undefined;
  #lastReply: string | undefined;
  #issue: string | undefined;

  constructor(document: OwnedDocument, identity: string, post: (json: string) => PromiseLike<boolean>) {
    this.#document = document;
    this.#identity = identity;
    this.#post = post;
  }
  get context(): Context {
    return { workspaceId: this.#identity, projectId: "stand-in", compilationContextId: "no-compiler",
      snapshot: { hostId: "vscode", documentUri: this.#document.uri, hostVersion: String(this.#document.version),
        snapshotId: `${this.#identity}-${this.#document.version}` } };
  }
  get lastPublication(): string | undefined { return this.#lastPublication; }
  get lastReply(): string | undefined { return this.#lastReply; }
  get issue(): string | undefined { return this.#issue; }

  async #send(message: ReturnType<typeof envelope>): Promise<void> {
    if (this.#disposed) return;
    try {
      const json = writeToSurface(message, schemas);
      if (message.family === "publish-graph") this.#lastPublication = json;
      if (message.family === "edit-result") this.#lastReply = json;
      if (!await this.#post(json)) this.#issue = "The page did not accept delivery; source was not retried.";
    } catch (error) {
      this.#issue = error instanceof IsolationError ? error.message : "Message delivery failed; source was not retried.";
    }
  }
  async publish(requestId?: string): Promise<void> {
    const projection = projectStandIn(this.#document.getText(), this.context, this.#document.version);
    await this.#send(envelope("publish-graph", requestId === undefined ? "notification" : "response",
      requestId ?? `host-${++this.#serial}`, { ...projection.graph, mode: "complete" }));
  }
  async changed(): Promise<void> {
    this.#proposal = undefined;
    await this.publish();
  }
  dispose(): void {
    this.#disposed = true;
    this.#proposal = undefined;
    this.#requests.clear();
  }
  async #reject(requestId: string, stage: "proposal" | "commit", reason: string): Promise<Handling> {
    await this.#send(envelope("edit-result", "response", requestId, {
      context: this.context, documents: [this.context.snapshot], changes: [], mappings: [], diagnostics: [],
      description: "The source edit was not applied.", stage, outcome: "rejected", rejectionReason: reason,
    }));
    return { accepted: false, reason, outcome: "rejected" };
  }

  async handle(raw: unknown): Promise<Handling> {
    // SEC-04 owns parsing, shape checks and the command boundary.
    const received = readFromSurface(raw, schemas);
    if (!received.accepted) return { accepted: false, reason: received.reason };
    if (this.#disposed) return { accepted: false, reason: "This editor is closed." };
    const message = received.message;
    const requestId = message["requestId"] as string;
    const version = message["version"] as { major: number; minor: number };
    if (version.major !== 0 || version.minor !== 0) return { accepted: false, reason: "This experiment speaks protocol 0.0." };
    if (this.#requests.has(requestId)) return { accepted: false, reason: "The request has already been handled." };
    this.#requests.add(requestId);

    if (received.family === "request-graph") {
      const payload = message["payload"] as { context: Context };
      if (!sameContext(payload.context, openingContext)) {
        const problem = snapshotProblem(payload.context.snapshot, this.context.snapshot);
        if (problem !== undefined || !sameContext(payload.context, this.context)) {
          return { accepted: false, reason: problem ?? "The editor context changed." };
        }
      }
      if (!this.#initialized) {
        this.#initialized = true;
        // SEC-04 has no surface-to-host initialize route. Both entries are
        // pinned to v0; this is an announcement, not version negotiation.
        await this.#send(envelope("initialize", "response", `host-${++this.#serial}`, {
          versions: [{ major: 0, minor: 0 }], components: [{ name: "vscode-stand-in", version: "0.0.0" }],
          hostId: "vscode", workspaceId: this.#identity, capabilities: ["graph.read", "edit.prepare", "edit.commit"], trustState: "trusted",
        }));
      }
      await this.publish(requestId);
      return { accepted: true };
    }
    if (received.family === "prepare-edit") {
      if (this.#applying) return this.#reject(requestId, "proposal", "An edit is still being applied.");
      const plan = planInsertion(this.#document.getText(), this.context, message["payload"] as unknown as Preparation);
      this.#proposal = typeof plan === "string" ? undefined : plan;
      if (typeof plan === "string") return this.#reject(requestId, "proposal", plan);
      await this.#send(envelope("edit-result", "response", requestId, plan));
      return { accepted: true, outcome: "proposed" };
    }
    if (received.family === "edit-result") {
      const commit = message["payload"] as unknown as Edit;
      const problem = editProblem(commit, this.context);
      if (problem !== undefined) return this.#reject(requestId, "commit", problem);
      if (this.#applying) return this.#reject(requestId, "commit", "An edit is still being applied.");
      const proposal = this.#proposal;
      this.#proposal = undefined;
      if (proposal === undefined || !matchesProposal(commit, proposal)) {
        return this.#reject(requestId, "commit", "Prepare this exact change again before applying it.");
      }
      this.#applying = true;
      let outcome: "applied" | "stale" | "failed";
      try {
        outcome = await this.#document.apply(Number(proposal.context.snapshot.hostVersion), proposal.changes);
      } catch {
        outcome = "failed";
      } finally {
        this.#applying = false;
      }
      if (outcome !== "applied") return this.#reject(requestId, "commit",
        outcome === "stale" ? "Stale document version." : "VS Code did not apply the edit; inspect the document before preparing again.");
      await this.#send(envelope("edit-result", "response", requestId, { ...proposal, stage: "commit", outcome: "applied" }));
      return { accepted: true, outcome: "applied" };
    }
    return { accepted: false, reason: "That operation is not implemented in this experiment." };
  }
}
