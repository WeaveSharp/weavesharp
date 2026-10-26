import { sameSnapshot, sameContext, type Context, type Snapshot } from "../../canvas/src/graph.ts";
import { projectStandIn } from "./stand-in.ts";
import type { Edit, Preparation } from "./wire.ts";

export function snapshotProblem(snapshot: Snapshot, current: Snapshot): string | undefined {
  if (snapshot.hostId !== current.hostId || snapshot.documentUri !== current.documentUri) return "Wrong document.";
  if (snapshot.hostVersion !== current.hostVersion) return "Stale document version.";
  if (snapshot.snapshotId !== current.snapshotId) return "The snapshot has been replaced.";
  return undefined;
}
export function editProblem(edit: Pick<Edit, "context" | "documents" | "changes">, current: Context): string | undefined {
  for (const snapshot of [edit.context.snapshot, ...edit.documents, ...edit.changes.map((change) => change.span.snapshot)]) {
    const problem = snapshotProblem(snapshot, current.snapshot);
    if (problem !== undefined) return problem;
  }
  if (!sameContext(edit.context, current)) return "The editor context changed.";
  if (edit.documents.length !== 1) return "This experiment edits one document.";
  return undefined;
}
export function planInsertion(source: string, current: Context, request: Preparation): Edit | string {
  const problem = editProblem({ context: request.context, documents: request.destinations,
    changes: request.anchors.map((span) => ({ span, text: "" })) }, current);
  if (problem !== undefined) return problem;
  if (request.command !== "insert-operation" || request.choices.find((choice) => choice.name === "operation")?.value !== "sample-step") {
    return "Only the empty-statement insertion is implemented.";
  }
  const projection = projectStandIn(source, current, 0);
  if (!projection.editable) return "Complete sample statements are needed for this edit.";
  const anchor = request.anchors[0];
  if (request.anchors.length !== 1 || anchor === undefined || !projection.statements.some((statement) =>
    statement.source.start === anchor.start && statement.source.length === anchor.length)) return "Choose a current statement as the insertion site.";
  const lineStart = source.lastIndexOf("\n", anchor.start - 1) + 1;
  const prefix = source.slice(lineStart, anchor.start);
  const separator = /^\s*$/.test(prefix) ? (source.includes("\r\n") ? "\r\n" : "\n") + prefix : " ";
  return { context: current, description: "Add an empty statement before the selected statement.",
    changes: [{ span: { ...anchor, length: 0 }, text: ";" + separator }], documents: [current.snapshot],
    mappings: [], diagnostics: [], stage: "proposal", outcome: "proposed" };
}
export function matchesProposal(commit: Edit, proposal: Edit): boolean {
  return sameContext(commit.context, proposal.context) && commit.documents.length === proposal.documents.length &&
    commit.documents.every((snapshot, index) => sameSnapshot(snapshot, proposal.documents[index]!)) &&
    commit.changes.length === proposal.changes.length && commit.changes.every((change, index) => {
      const planned = proposal.changes[index]!;
      return sameSnapshot(change.span.snapshot, planned.span.snapshot) && change.span.start === planned.span.start &&
        change.span.length === planned.span.length && change.text === planned.text;
    });
}
