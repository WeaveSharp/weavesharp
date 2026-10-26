import type { Context, Snapshot, Span } from "../../canvas/src/graph.ts";

export interface Change { readonly span: Span; readonly text: string }
export interface Preparation {
  readonly context: Context;
  readonly command: string;
  readonly anchors: readonly Span[];
  readonly choices: readonly { readonly name: string; readonly value: string }[];
  readonly destinations: readonly Snapshot[];
}
export interface Edit {
  readonly context: Context;
  readonly description: string;
  readonly changes: readonly Change[];
  readonly documents: readonly Snapshot[];
  readonly mappings: readonly unknown[];
  readonly diagnostics: readonly unknown[];
  readonly stage: "proposal" | "commit";
  readonly outcome: "proposed" | "requested" | "applied" | "rejected";
  readonly rejectionReason?: string;
}
export function envelope(family: string, kind: string, requestId: string, payload: unknown) {
  return { version: { major: 0, minor: 0 }, family, kind, requestId, payload, extensions: {} };
}

// A new page has no document identity yet. This read request asks its editor
// for the first publication; it never selects or opens a document.
export const openingContext: Context = {
  workspaceId: "opening-view", projectId: "stand-in", compilationContextId: "no-compiler",
  snapshot: { hostId: "vscode", documentUri: "weavesharp://opening-view", hostVersion: "0", snapshotId: "opening-view" },
};
export function graphRequest(requestId: string, context: Context = openingContext) {
  return envelope("request-graph", "query", requestId, {
    context, body: { snapshot: context.snapshot, coordinates: "utf16-offset", start: 0, length: 0 },
    detail: "full", requireComplete: true,
  });
}
export function insertionRequest(requestId: string, context: Context, anchor: Span) {
  return envelope("prepare-edit", "query", requestId, {
    context, command: "insert-operation", anchors: [anchor],
    choices: [{ name: "operation", value: "sample-step" }], destinations: [context.snapshot],
  });
}
export function commitRequest(requestId: string, proposal: Edit) {
  return envelope("edit-result", "mutation", requestId, { ...proposal, stage: "commit", outcome: "requested" });
}
