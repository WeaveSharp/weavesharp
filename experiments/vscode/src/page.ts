import { createNonce, surfacePage, type PageOptions } from "../../../src/surface/src/isolation/index.ts";

export function pageFor(script: string, style: string): { readonly options: PageOptions; readonly html: string } {
  const options: PageOptions = { nonce: createNonce(), resourceOrigins: ["https://*.vscode-cdn.net"],
    scripts: [script], styles: [style], title: "WeaveSharp graph experiment" };
  return { options, html: surfacePage(options) };
}
