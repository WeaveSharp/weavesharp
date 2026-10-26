import assert from "node:assert/strict";
import { readInbound, writeOutbound } from "../../../src/surface/src/isolation/index.ts";
import { GraphEditor, type OwnedDocument } from "../src/editor.ts";
import { schemas } from "../src/schemas.generated.ts";
import { insertionRequest, type Change, type Edit } from "../src/wire.ts";
import type { Graph } from "../../canvas/src/graph.ts";

export const sample = "class CounterDemo\n{\n    static void Run()\n    {\n        int total = 3;\n        total++;\n        total--;\n    }\n}\n";
export class MemoryDocument implements OwnedDocument {
  readonly uri: string;
  version = 1;
  text: string;
  applied: { version: number; changes: readonly Change[] }[] = [];
  onChange: () => Promise<void> = async () => {};
  beforeApply: () => void = () => {};
  failure: "failed" | "throw" | undefined;
  readonly #undo: string[] = [];
  readonly #redo: string[] = [];
  constructor(text = sample, uri = "file:///workspace/CounterDemo.cs") { this.text = text; this.uri = uri; }
  getText(): string { return this.text; }
  async apply(version: number, changes: readonly Change[]): Promise<"applied" | "stale" | "failed"> {
    this.beforeApply();
    if (version !== this.version) return "stale";
    if (this.failure === "throw") throw new Error("Apply unavailable");
    if (this.failure === "failed") return "failed";
    this.applied.push({ version, changes });
    this.#undo.push(this.text);
    this.#redo.length = 0;
    for (const change of [...changes].sort((a, b) => b.span.start - a.span.start)) {
      this.text = this.text.slice(0, change.span.start) + change.text + this.text.slice(change.span.start + change.span.length);
    }
    this.version++;
    await this.onChange();
    return "applied";
  }
  async undo(): Promise<void> {
    const previous = this.#undo.pop();
    assert.notEqual(previous, undefined);
    this.#redo.push(this.text);
    this.text = previous!;
    this.version++;
    await this.onChange();
  }
  async redo(): Promise<void> {
    const next = this.#redo.pop();
    assert.notEqual(next, undefined);
    this.#undo.push(this.text);
    this.text = next!;
    this.version++;
    await this.onChange();
  }
}
export function rig(text = sample) {
  const document = new MemoryDocument(text);
  const posted: string[] = [];
  const editor = new GraphEditor(document, "test-view", async (json) => { posted.push(json); return true; });
  document.onChange = () => editor.changed();
  return { document, editor, posted };
}
export function payload<T>(json: string | undefined): T {
  assert.notEqual(json, undefined);
  const result = readInbound(json, schemas);
  if (!result.accepted) throw new Error(result.reason);
  return result.message["payload"] as T;
}
export async function prepare(editor: GraphEditor, requestId = "prepare-1"): Promise<Edit> {
  await editor.publish();
  const graph = payload<Graph>(editor.lastPublication);
  const anchor = graph.mappings[0]?.source;
  assert.notEqual(anchor, undefined);
  const result = await editor.handle(writeOutbound(insertionRequest(requestId, graph.context, anchor!), schemas));
  assert.equal(result.outcome, "proposed");
  return payload<Edit>(editor.lastReply);
}
