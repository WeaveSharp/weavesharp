import { element, label, type DocumentLike, type ElementLike } from "../../../src/surface/src/isolation/index.ts";
import { KeyboardSession } from "../../canvas/src/commands.ts";
import { receiveGraph, type Graph } from "../../canvas/src/graph.ts";
import { schemas } from "../../vscode/src/schemas.generated.ts";
import { receiveBase64 } from "./receive.ts";

export const editNotice = "Editing in this view is disabled. Use Rider's Insert sample statement action in the C# text editor.";

export function commitControl(document: DocumentLike): ElementLike {
  return element(document, "button", { type: "button", disabled: "disabled", "aria-describedby": "editing-note" },
    [label(document, "Apply graph edit (unavailable)")]);
}

export function diagnosticList(document: DocumentLike, graph: Graph): ElementLike {
  return element(document, "ul", { "aria-label": "Document diagnostics" }, graph.diagnostics.map((diagnostic) =>
    element(document, "li", {}, [label(document, `${diagnostic.severity}: ${diagnostic.code}: ${diagnostic.message}`)])));
}

export class RiderSurface {
  #session: KeyboardSession | undefined;
  #notice = "Awaiting a document snapshot from Rider.";
  get session(): KeyboardSession | undefined { return this.#session; }
  get notice(): string { return this.#notice; }

  receive(encoded: string): boolean {
    const incoming = receiveBase64(encoded);
    if (!incoming.result.accepted) { this.#notice = incoming.result.reason; return false; }
    if (incoming.result.family === "initialize") return true;
    if (incoming.result.family !== "publish-graph") return false;
    const next = receiveGraph(incoming.text, schemas, this.#session?.graph);
    if (!next.ok) { this.#notice = next.reason; return false; }
    // Publications are complete and serial within one editor lifetime.
    if (this.#session !== undefined && next.graph.revision <= this.#session.graph.revision) return false;
    if (this.#session === undefined) this.#session = new KeyboardSession(next.graph, schemas);
    else this.#session.synchronize(next.graph);
    this.#notice = next.graph.coverage === "complete" ? "Document snapshot displayed using the sample grammar." :
      "This document exceeds the sample grammar. Inspect its diagnostics or use the text editor.";
    return true;
  }

  commit(): false { this.#notice = editNotice; return false; }
}
