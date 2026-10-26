import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import type { DocumentLike, ElementLike, NodeLike } from "../../../src/surface/src/isolation/index.ts";

export const repositoryRoot = fileURLToPath(new URL("../../../", import.meta.url));
export function schemas(): ReadonlyMap<string, unknown> {
  const directory = join(repositoryRoot, "protocol", "v0");
  return new Map(readdirSync(directory).filter((name) => name.endsWith(".schema.json"))
    .map((name) => [name, JSON.parse(readFileSync(join(directory, name), "utf8")) as unknown]));
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

// Local recording document, adapted from the surface's test fixture.
export class FakeDocument implements DocumentLike {
  readonly created: (FakeElement | FakeText)[] = [];
  createElementNS(namespace: string, name: string): FakeElement {
    const created: FakeElement = {
      kind: "element", nodeType: 1, namespace, name, attributes: new Map(), children: [],
      setAttribute(attribute: string, value: string) { this.attributes.set(attribute, value); },
      append(...nodes: NodeLike[]) { this.children.push(...nodes as (FakeElement | FakeText)[]); },
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

export function elements(root: ElementLike): FakeElement[] {
  const node = root as FakeElement;
  return [node, ...node.children.flatMap((child) => child.kind === "element" ? elements(child) : [])];
}
export function shownText(node: FakeElement | FakeText): string {
  return node.kind === "text" ? node.data : node.children.map(shownText).join("");
}
