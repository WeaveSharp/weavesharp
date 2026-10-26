import { test } from "node:test";
import assert from "node:assert/strict";
import { element, label, text, IsolationError } from "../src/isolation/index.ts";
import { FakeDocument, shownText, type FakeElement } from "./support.ts";

const HOSTILE = [
  '<img src=x onerror="alert(1)">',
  "<script>alert(1)</script>",
  '"><svg onload=alert(1)>',
  "javascript:alert(1)",
  "{{constructor.constructor('alert(1)')()}}",
  "</span><iframe srcdoc='x'>",
];

for (const value of HOSTILE) {
  test(`hostile text stays text in a label: ${value}`, () => {
    const document = new FakeDocument();
    const created = label(document, value) as FakeElement;
    assert.equal(shownText(created), value);
    // Only the label's own span was created as an element; the rest are text nodes.
    assert.deepEqual(document.created.filter((n) => n.kind === "element").map((n) => (n as FakeElement).name), ["span"]);
    assert.equal(created.attributes.get("dir"), "auto");
  });

  test(`hostile text stays text in a text node and an aria label: ${value}`, () => {
    const document = new FakeDocument();
    const node = text(document, value);
    assert.equal((node as unknown as { data: string }).data, value);
    const created = element(document, "div", { "aria-label": value }, [value]) as FakeElement;
    assert.equal(created.attributes.get("aria-label"), value);
    assert.equal(shownText(created), value);
  });
}

test("a label with hidden characters is flagged and names them for assistive technology", () => {
  const document = new FakeDocument();
  const created = label(document, "a‮b\u0007") as FakeElement;
  assert.equal(created.attributes.get("class"), "ws-label ws-label-flagged");
  const badge = created.children[1] as FakeElement;
  assert.equal(badge.attributes.get("role"), "img");
  assert.equal(badge.attributes.get("aria-label"), "Contains 1 hidden text-direction character and 1 control character");
});

for (const name of ["script", "style", "iframe", "object", "embed", "link", "meta", "base", "a", "img", "form", "input", "foreignObject", "use", "image", "animate", "set"]) {
  test(`the builder refuses the ${name} element`, () => {
    assert.throws(() => element(new FakeDocument(), name), IsolationError);
  });
}

for (const [name, attribute, value] of [
  ["div", "onclick", "alert(1)"],
  ["span", "onmouseover", "x"],
  ["svg", "onload", "x"],
  ["div", "style", "background:url(javascript:alert(1))"],
  ["div", "href", "javascript:alert(1)"],
  ["div", "src", "x"],
  ["path", "xlink:href", "#x"],
  ["div", "srcdoc", "x"],
  ["button", "formaction", "x"],
  ["div", "data-other", "x"],
  ["div", "dir", "rtl;x"],
  ["div", "class", "a b\" onclick=\"x"],
  ["div", "tabindex", "1e3"],
  ["path", "d", "M0 0 url(x)"],
  ["g", "transform", "matrix(1,0,0,1,0,0) expression(x)"],
  ["button", "type", "submit"],
] as const) {
  test(`the builder refuses ${attribute}=${JSON.stringify(value)} on ${name}`, () => {
    assert.throws(() => element(new FakeDocument(), name, { [attribute]: value }), IsolationError);
  });
}

test("allowed elements and attributes are created in their namespaces", () => {
  const document = new FakeDocument();
  const svg = element(document, "svg", { viewBox: "0 0 100 50", focusable: "false", role: "img", "aria-label": "Graph" }, [
    element(document, "path", { d: "M0 0 L10 10 Z", class: "ws-edge ws-edge-execution", "data-ws-edge": "e1" }),
  ]) as FakeElement;
  assert.equal(svg.namespace, "http://www.w3.org/2000/svg");
  assert.equal((svg.children[0] as FakeElement).attributes.get("data-ws-edge"), "e1");
  const div = element(document, "div", { dir: "rtl", tabindex: "0" }) as FakeElement;
  assert.equal(div.namespace, "http://www.w3.org/1999/xhtml");
});

test("tooltips and accessible names get a label's markers, so assistive technology announces hidden characters too", () => {
  const document = new FakeDocument();
  for (const attribute of ["title", "aria-label", "aria-description", "aria-roledescription", "aria-valuetext"]) {
    const created = element(document, "div", { [attribute]: "admin‮​" }) as FakeElement;
    assert.equal(created.attributes.get(attribute), "admin⟦U+202E⟧⟦U+200B⟧", attribute);
  }
  const long = element(document, "div", { "aria-label": "x".repeat(600) }) as FakeElement;
  assert.equal([...(long.attributes.get("aria-label") ?? "")].length, 513);
  const reference = element(document, "div", { "aria-labelledby": "graph-label-1" }) as FakeElement;
  assert.equal(reference.attributes.get("aria-labelledby"), "graph-label-1");
});

test("a label names invisible characters in its badge", () => {
  const created = label(new FakeDocument(), "to​tal") as FakeElement;
  const badge = created.children[1] as FakeElement;
  assert.equal(badge.attributes.get("aria-label"), "Contains 1 invisible character");
});

test("every announced ARIA attribute gets markers; reference lists hold identifiers only", () => {
  const document = new FakeDocument();
  for (const attribute of ["aria-rowindextext", "aria-colindextext", "aria-keyshortcuts", "aria-valuetext", "aria-placeholder"]) {
    const created = element(document, "div", { [attribute]: "row‮1" }) as FakeElement;
    assert.equal(created.attributes.get(attribute), "row⟦U+202E⟧1", attribute);
  }
  const states = element(document, "li", { "aria-expanded": "true", "aria-level": "2", "aria-live": "polite" }) as FakeElement;
  assert.equal(states.attributes.get("aria-expanded"), "true");
  const references = element(document, "div", { "aria-labelledby": "graph-label-1 graph-label-2" }) as FakeElement;
  assert.equal(references.attributes.get("aria-labelledby"), "graph-label-1 graph-label-2");
  for (const value of ["label‮", "a  b", "javascript:x()", "a<b"]) {
    assert.throws(() => element(document, "div", { "aria-describedby": value }), IsolationError, value);
  }
});
