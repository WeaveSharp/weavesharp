import { readInbound, type InboundResult } from "../../../src/surface/src/isolation/index.ts";
import { schemas } from "../../vscode/src/schemas.generated.ts";

export function receiveBase64(encoded: string): { readonly text: string; readonly result: InboundResult } {
  let text: string;
  try {
    const binary = atob(encoded);
    text = new TextDecoder("utf-8", { fatal: true }).decode(Uint8Array.from(binary, (unit) => unit.charCodeAt(0)));
  } catch {
    return { text: "", result: { accepted: false, reason: "The host delivery could not be decoded." } };
  }
  return { text, result: readInbound(text, schemas) };
}
