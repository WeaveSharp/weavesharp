import * as vscode from "vscode";
import { GraphEditor, type OwnedDocument } from "./editor.ts";
import { pageFor } from "./page.ts";
import { DebugObserver } from "./debug-observer.ts";

export const viewType = "weavesharp.graph";
export interface EditorView {
  readonly controller: GraphEditor;
  readonly panel: vscode.WebviewPanel;
  readonly page: ReturnType<typeof pageFor>;
  reload(): void;
}
export interface SpikeApi {
  readonly views: ReadonlyMap<string, EditorView>;
  readonly debugSessions: ReadonlyMap<string, DebugObserver>;
}

export function activate(context: vscode.ExtensionContext): SpikeApi {
  const views = new Map<string, EditorView>();
  const debugSessions = new Map<string, DebugObserver>();
  let serial = 0;
  context.subscriptions.push(vscode.window.registerCustomEditorProvider(viewType, {
    resolveCustomTextEditor(document, panel) {
      const webviewFolder = vscode.Uri.joinPath(context.extensionUri, "out", "webview");
      panel.webview.options = { enableScripts: true, enableForms: false, enableCommandUris: false,
        localResourceRoots: [webviewFolder] };
      const script = panel.webview.asWebviewUri(vscode.Uri.joinPath(webviewFolder, "experiments", "vscode", "src", "webview.js")).toString();
      const style = panel.webview.asWebviewUri(vscode.Uri.joinPath(webviewFolder, "experiments", "canvas", "src", "canvas.css")).toString();
      const owned: OwnedDocument = {
        uri: document.uri.toString(),
        get version() { return document.version; },
        getText: () => document.getText(),
        apply(expected, changes) {
          if (document.isClosed || document.version !== expected) return Promise.resolve("stale");
          const edit = new vscode.WorkspaceEdit();
          for (const change of changes) {
            edit.replace(document.uri, new vscode.Range(document.positionAt(change.span.start),
              document.positionAt(change.span.start + change.span.length)), change.text);
          }
          // No await occurs between checking the model and handing VS Code
          // this one text transaction. applyEdit owns the actual undo entry.
          return vscode.workspace.applyEdit(edit).then((applied) => applied ? "applied" as const : "failed" as const);
        },
      };
      let controller: GraphEditor;
      let page: ReturnType<typeof pageFor>;
      const view: EditorView = {
        get controller() { return controller; }, panel, get page() { return page; },
        reload() {
          controller?.dispose();
          controller = new GraphEditor(owned, `editor-${++serial}`, (json) => panel.webview.postMessage(json));
          page = pageFor(script, style);
          panel.webview.html = page.html;
        },
      };
      view.reload();
      views.set(owned.uri, view);
      let wasVisible = panel.visible;
      const subscriptions = [
        panel.webview.onDidReceiveMessage((raw: unknown) => { void controller.handle(raw); }),
        vscode.workspace.onDidChangeTextDocument((event) => {
          if (event.document === document) void controller.changed();
        }),
        panel.onDidChangeViewState(() => {
          // A hidden page is discarded. Store a newly generated page for its
          // next reveal instead of reusing the previous load's nonce.
          if (wasVisible && !panel.visible) view.reload();
          if (!wasVisible && panel.visible) void controller.publish();
          wasVisible = panel.visible;
        }),
      ];
      panel.onDidDispose(() => {
        controller.dispose();
        for (const subscription of subscriptions) subscription.dispose();
        if (views.get(owned.uri) === view) views.delete(owned.uri);
      });
    },
  }, { webviewOptions: { retainContextWhenHidden: false }, supportsMultipleEditorsPerDocument: false }));
  context.subscriptions.push(vscode.debug.registerDebugAdapterTrackerFactory("*", {
    createDebugAdapterTracker(session) {
      const observer = new DebugObserver(session.id);
      debugSessions.set(session.id, observer);
      return { onWillReceiveMessage: (message) => observer.receive(message), onDidSendMessage: (message) => observer.send(message),
        onWillStopSession: () => { observer.clear(); debugSessions.delete(session.id); },
        onError: () => observer.clear(), onExit: () => { observer.clear(); debugSessions.delete(session.id); } };
    },
  }));
  context.subscriptions.push({ dispose() {
    for (const view of views.values()) view.controller.dispose();
    views.clear();
    for (const observer of debugSessions.values()) observer.clear();
    debugSessions.clear();
  } });
  return { views, debugSessions };
}
