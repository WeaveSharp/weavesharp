import assert from "node:assert/strict";
import * as vscode from "vscode";
import { readInbound, surfacePage, writeOutbound } from "../../../../src/surface/src/isolation/index.ts";
import type { Graph } from "../../../canvas/src/graph.ts";
import type { EditorView, SpikeApi } from "../../src/extension.ts";
import { schemas } from "../../src/schemas.generated.ts";
import { commitRequest, graphRequest, insertionRequest, type Edit } from "../../src/wire.ts";

async function until(predicate: () => boolean, message: string): Promise<void> {
  const deadline = Date.now() + 10000;
  while (!predicate()) {
    assert.ok(Date.now() < deadline, message);
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}
function payload<T>(json: string | undefined): T {
  assert.notEqual(json, undefined);
  const result = readInbound(json, schemas);
  if (!result.accepted) throw new Error(result.reason);
  return result.message["payload"] as T;
}
function pageChecks(view: EditorView, extensionUri: vscode.Uri): void {
  assert.equal(view.panel.webview.html, surfacePage(view.page.options));
  assert.equal(view.panel.webview.html, view.page.html);
  assert.deepEqual(view.page.options.resourceOrigins, ["https://*.vscode-cdn.net"]);
  const folder = vscode.Uri.joinPath(extensionUri, "out", "webview");
  assert.deepEqual(view.panel.webview.options, {
    enableScripts: true, enableForms: false, enableCommandUris: false, localResourceRoots: [folder],
  });
  assert.equal(view.panel.options.retainContextWhenHidden, false);
  assert.deepEqual(view.page.options.scripts, [view.panel.webview.asWebviewUri(vscode.Uri.joinPath(folder,
    "experiments", "vscode", "src", "webview.js")).toString()]);
  assert.deepEqual(view.page.options.styles, [view.panel.webview.asWebviewUri(vscode.Uri.joinPath(folder,
    "experiments", "canvas", "src", "canvas.css")).toString()]);
}

export async function run(): Promise<void> {
  const extension = vscode.extensions.all.find((candidate) => candidate.packageJSON.name === "weavesharp-vscode-spike");
  assert.ok(extension, "The development extension must be installed.");
  const api = await extension.activate() as SpikeApi;
  const uri = vscode.Uri.joinPath(extension.extensionUri, "test", "fixtures", "Sample.cs");
  const document = await vscode.workspace.openTextDocument(uri);
  const original = document.getText();
  await vscode.commands.executeCommand("vscode.openWith", uri, "weavesharp.graph");
  await until(() => api.views.has(uri.toString()), "The custom editor did not resolve.");
  const view = api.views.get(uri.toString())!;
  const changes: { version: number; reason: vscode.TextDocumentChangeReason | undefined }[] = [];
  const listener = vscode.workspace.onDidChangeTextDocument((event) => {
    if (event.document === document && event.contentChanges.length > 0) changes.push({ version: document.version, reason: event.reason });
  });
  try {
    pageChecks(view, extension.extensionUri);
    const nonce = view.page.options.nonce;
    view.reload();
    pageChecks(view, extension.extensionUri);
    assert.notEqual(view.page.options.nonce, nonce);

    assert.equal((await view.controller.handle(writeOutbound(graphRequest("integration-opening"), schemas))).accepted, true);
    const graph = payload<Graph>(view.controller.lastPublication);
    assert.equal(graph.context.snapshot.documentUri, uri.toString());
    assert.equal(graph.context.snapshot.hostVersion, String(document.version));
    assert.equal(graph.nodes.length, 3);
    const preparation = insertionRequest("integration-prepare", graph.context, graph.mappings[0]!.source);
    assert.equal((await view.controller.handle(writeOutbound(preparation, schemas))).outcome, "proposed");
    const proposal = payload<Edit>(view.controller.lastReply);
    assert.equal(document.getText(), original);
    const change = proposal.changes[0]!;
    const edited = original.slice(0, change.span.start) + change.text + original.slice(change.span.start + change.span.length);
    const before = document.version;
    assert.equal((await view.controller.handle(writeOutbound(commitRequest("integration-commit", proposal), schemas))).outcome, "applied");
    await until(() => document.getText() === edited, "The commit did not reach the document.");
    assert.ok(document.version > before);
    assert.equal(changes.length, 1, "One text transaction should be observed.");
    assert.equal(changes[0]?.reason, undefined);
    await until(() => payload<Graph>(view.controller.lastPublication).context.snapshot.hostVersion === String(document.version), "The edit graph is stale.");
    assert.equal(payload<Graph>(view.controller.lastPublication).nodes.length, 4);

    const appliedVersion = document.version;
    const stale = await view.controller.handle(writeOutbound(commitRequest("integration-stale", proposal), schemas));
    assert.equal(stale.outcome, "rejected");
    assert.match(stale.reason!, /Stale/);
    assert.equal(document.version, appliedVersion);
    assert.equal(document.getText(), edited);

    await vscode.commands.executeCommand("undo");
    await until(() => document.getText() === original, "VS Code undo did not restore the source.");
    assert.ok(document.version > appliedVersion);
    assert.equal(changes.at(-1)?.reason, vscode.TextDocumentChangeReason.Undo);
    await until(() => payload<Graph>(view.controller.lastPublication).context.snapshot.hostVersion === String(document.version), "Undo was not republished.");
    assert.equal(payload<Graph>(view.controller.lastPublication).nodes.length, 3);

    const undoVersion = document.version;
    await vscode.commands.executeCommand("redo");
    await until(() => document.getText() === edited, "VS Code redo did not reapply the source.");
    assert.ok(document.version > undoVersion);
    assert.equal(changes.at(-1)?.reason, vscode.TextDocumentChangeReason.Redo);
    await until(() => payload<Graph>(view.controller.lastPublication).context.snapshot.hostVersion === String(document.version), "Redo was not republished.");
    assert.equal(payload<Graph>(view.controller.lastPublication).nodes.length, 4);
    console.log("VS Code integration: page, nonce, options, edit, stale refusal, undo, redo and graph checks passed.");
  } finally {
    listener.dispose();
    if (document.getText() !== original) {
      const restore = new vscode.WorkspaceEdit();
      restore.replace(uri, new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), original);
      assert.equal(await vscode.workspace.applyEdit(restore), true);
    }
    if (document.isDirty) assert.equal(await document.save(), true);
    view.panel.dispose();
  }
}
