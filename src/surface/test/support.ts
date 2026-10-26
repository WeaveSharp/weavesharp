import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { DocumentLike, ElementLike, NodeLike } from "../src/isolation/index.ts";

const here = dirname(fileURLToPath(import.meta.url));
export const repositoryRoot = join(here, "..", "..", "..");

/** Protocol v0's schemas, read from the repository as the surface's bundle will embed them. */
export function loadProtocolSchemas(): ReadonlyMap<string, unknown> {
  const folder = join(repositoryRoot, "protocol", "v0");
  return new Map(
    readdirSync(folder)
      .filter((name) => name.endsWith(".schema.json"))
      .map((name) => [name, JSON.parse(readFileSync(join(folder, name), "utf8")) as unknown]),
  );
}

/** Protocol v0's example messages, written for WS-06's tests. */
export function protocolExamples(): { family: string; name: string; text: string }[] {
  const folder = join(repositoryRoot, "tests", "WeaveSharp.Protocol.Tests", "Examples");
  return readdirSync(folder).flatMap((family) =>
    readdirSync(join(folder, family)).map((name) => ({ family, name, text: readFileSync(join(folder, family, name), "utf8") })),
  );
}

export interface FakeText extends NodeLike {
  readonly kind: "text";
  readonly data: string;
}

export interface FakeElement extends ElementLike {
  readonly kind: "element";
  readonly namespace: string;
  readonly name: string;
  readonly attributes: Map<string, string>;
  readonly children: (FakeElement | FakeText)[];
}

/** A document that records what the surface asks of it, and parses nothing. */
export class FakeDocument implements DocumentLike {
  readonly created: (FakeElement | FakeText)[] = [];

  createElementNS(namespace: string, name: string): FakeElement {
    const created: FakeElement = {
      kind: "element",
      nodeType: 1,
      namespace,
      name,
      attributes: new Map(),
      children: [],
      setAttribute(attribute: string, value: string) {
        this.attributes.set(attribute, value);
      },
      append(...nodes: NodeLike[]) {
        this.children.push(...(nodes as (FakeElement | FakeText)[]));
      },
    };
    this.created.push(created);
    return created;
  }

  createTextNode(data: string): FakeText {
    const created: FakeText = { kind: "text", nodeType: 3, data };
    this.created.push(created);
    return created;
  }
}

/** The text a subtree would show, concatenated in order. */
export function shownText(node: FakeElement | FakeText): string {
  return node.kind === "text" ? node.data : node.children.map(shownText).join("");
}
