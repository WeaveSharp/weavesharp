// A projection of VS Code's adapter notifications, not a DAP implementation.
// Nothing here requests stack frames, evaluates expressions or opens sources.
export interface DebugSource { readonly name?: string; readonly path?: string; readonly sourceReference?: number }
export interface ReportedFrame {
  readonly id: number;
  readonly name: string;
  readonly source?: DebugSource;
  readonly line?: number;
  readonly column?: number;
  readonly endLine?: number;
  readonly endColumn?: number;
}
export interface AdapterMessage {
  readonly type: string;
  readonly seq?: number;
  readonly request_seq?: number;
  readonly command?: string;
  readonly event?: string;
  readonly success?: boolean;
  readonly arguments?: { readonly threadId?: number; readonly linesStartAt1?: boolean; readonly columnsStartAt1?: boolean };
  readonly body?: { readonly reason?: string; readonly threadId?: number; readonly allThreadsStopped?: boolean;
    readonly stackFrames?: readonly ReportedFrame[] };
}
export interface StopObservation {
  readonly session: string;
  readonly pause: number;
  readonly reason?: string;
  readonly threadId?: number;
  readonly allThreadsStopped?: boolean;
}
export interface FrameObservation extends ReportedFrame {
  readonly session: string;
  readonly pause: number;
  readonly threadId?: number;
  readonly lineBase: 0 | 1;
  readonly columnBase: 0 | 1;
}
export class DebugObserver {
  readonly session: string;
  readonly #pending = new Map<number, { pause: number; threadId?: number }>();
  #pause = 0;
  #lineBase: 0 | 1 = 1;
  #columnBase: 0 | 1 = 1;
  #stop: StopObservation | undefined;
  #frames: FrameObservation[] = [];
  constructor(session: string) { this.session = session; }
  get stopped(): StopObservation | undefined { return this.#stop; }
  get frames(): readonly FrameObservation[] { return [...this.#frames]; }
  receive(message: AdapterMessage): void {
    if (message.type !== "request") return;
    if (message.command === "initialize") {
      this.#lineBase = message.arguments?.linesStartAt1 === false ? 0 : 1;
      this.#columnBase = message.arguments?.columnsStartAt1 === false ? 0 : 1;
    }
    if (message.command === "stackTrace" && message.seq !== undefined && this.#stop !== undefined) {
      this.#pending.set(message.seq, { pause: this.#pause, threadId: message.arguments?.threadId });
    }
    if (["continue", "next", "stepIn", "stepOut", "restart", "disconnect"].includes(message.command ?? "")) this.clear();
  }
  send(message: AdapterMessage): void {
    if (message.type === "event") {
      if (message.event === "stopped") {
        this.clear();
        this.#stop = { session: this.session, pause: ++this.#pause, reason: message.body?.reason,
          threadId: message.body?.threadId, allThreadsStopped: message.body?.allThreadsStopped };
      } else if (["continued", "terminated", "exited"].includes(message.event ?? "")) this.clear();
    }
    if (message.type !== "response" || message.command !== "stackTrace" || message.request_seq === undefined) return;
    const pending = this.#pending.get(message.request_seq);
    this.#pending.delete(message.request_seq);
    if (pending === undefined || pending.pause !== this.#stop?.pause || message.success !== true) return;
    const frames = (message.body?.stackFrames ?? []).map((frame): FrameObservation => ({
      session: this.session, pause: pending.pause, threadId: pending.threadId, lineBase: this.#lineBase, columnBase: this.#columnBase,
      id: frame.id, name: frame.name, source: frame.source === undefined ? undefined : {
        name: frame.source.name, path: frame.source.path, sourceReference: frame.source.sourceReference },
      line: frame.line, column: frame.column, endLine: frame.endLine, endColumn: frame.endColumn,
    }));
    this.#frames = [...this.#frames.filter((frame) => frame.threadId !== pending.threadId), ...frames];
  }
  clear(): void {
    this.#stop = undefined;
    this.#frames = [];
    this.#pending.clear();
  }
}
