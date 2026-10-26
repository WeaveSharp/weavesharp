import { contentSecurityPolicy, surfacePage, type PageOptions } from "../../../src/surface/src/isolation/index.ts";

export const origin = "https://surface.weavesharp.invalid";
export const noncePlaceholder = "AAAAAAAAAAAAAAAAAAAAAA==";
export const pageOptions: PageOptions = {
  nonce: noncePlaceholder,
  resourceOrigins: [origin],
  scripts: [`${origin}/experiments/rider/webview/entry.js`],
  styles: [`${origin}/experiments/canvas/src/canvas.css`],
  title: "Rider graph trial",
};

export function buildPage(): { readonly html: string; readonly policy: string } {
  return {
    html: surfacePage(pageOptions),
    policy: `${contentSecurityPolicy(pageOptions)}; frame-ancestors 'none'`,
  };
}

// Only this build-owned inventory is packaged for the browser.
export const browserFiles = [
  "experiments/rider/webview/entry.js",
  "experiments/rider/webview/client.js",
  "experiments/rider/webview/receive.js",
  "experiments/vscode/src/schemas.generated.js",
  "experiments/canvas/src/commands.js",
  "experiments/canvas/src/graph.js",
  "experiments/canvas/src/layout.js",
  "experiments/canvas/src/render.js",
  "experiments/canvas/src/canvas.css",
  ...["index", "csp", "dom", "errors", "messages", "page", "schema", "text"]
    .map((name) => `src/surface/src/isolation/${name}.js`),
] as const;
