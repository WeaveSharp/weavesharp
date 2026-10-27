import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { scanSource } from "../src/isolation/sinks.ts";
import { repositoryRoot } from "./support.ts";

function sourceFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? sourceFiles(join(folder, entry.name)) : entry.name.endsWith(".ts") ? [join(folder, entry.name)] : [],
  );
}

test("the surface's source uses no HTML, code or navigation sink", () => {
  const files = sourceFiles(join(repositoryRoot, "src", "surface", "src")).filter((f) => !f.endsWith("sinks.ts"));
  assert.ok(files.length >= 6);
  const findings = files.flatMap((file) => scanSource(file, readFileSync(file, "utf8")));
  assert.deepEqual(findings, []);
});

for (const [sink, line] of [
  ["innerHTML", "node.innerHTML = value;"],
  ["outerHTML", "node.outerHTML = value;"],
  ["insertAdjacentHTML", "node.insertAdjacentHTML('beforeend', value);"],
  ["document.write", "document.write(value);"],
  ["eval", "eval(code);"],
  ["Function constructor", "const f = new Function('return 1');"],
  ["string timer", "setTimeout('alert(1)', 0);"],
  ["srcdoc", "frame.srcdoc = value;"],
  ["DOMParser", "new DOMParser().parseFromString(value, 'text/html');"],
  ["createContextualFragment", "range.createContextualFragment(value);"],
  ["URL property assignment", "anchor.href = value;"],
  ["window.open", "window.open(value);"],
  ["navigation", "location.assign(value);"],
  ["importScripts", "importScripts(value);"],
  ["dynamic import", "await import(value);"],
  ["setAttribute outside the DOM builder", "node.setAttribute('onclick', value);"],
  ["style or script element", "document.createElement('script');"],
] as const) {
  test(`the scan finds ${sink}`, () => {
    assert.deepEqual(scanSource("example.ts", line).map((f) => f.sink), [sink]);
  });
}

test("setAttribute is allowed only in the DOM builder, matched by its repository path", () => {
  assert.deepEqual(scanSource("src/surface/src/isolation/dom.ts", "created.setAttribute(attribute, value);"), []);
  assert.deepEqual(scanSource("/checkout/src/surface/src/isolation/dom.ts", "created.setAttribute(attribute, value);"), []);
  for (const elsewhere of ["src/isolation/dom.ts", "experiments/canvas/src/dom.ts", "experiments/x/src/surface/src/isolation/dom.ts.bak"]) {
    assert.deepEqual(scanSource(elsewhere, "created.setAttribute(attribute, value);").map((f) => f.sink), ["setAttribute outside the DOM builder"], elsewhere);
  }
});

// Routes the first scan missed (the Reviewer's review 01M5WSE5K4WPYPY5V9PAHDKWK9).
for (const [sinks, source] of [
  [["computed sink member"], 'node["innerHTML"] = value;'],
  [["computed sink member"], "document['write'](value);"],
  [["computed sink member"], "anchor[`href`] = value;"],
  [["reflective write"], "Reflect.set(node, name, value);"],
  [["reflective write"], "Object.assign(node, { innerHTML: value });"],
  [["setAttribute outside the DOM builder"], "node.setAttributeNS(null, 'onclick', value);"],
  [["setAttribute outside the DOM builder"], "node.setAttributeNode(attribute);"],
  [["setAttribute outside the DOM builder"], "node.attributes.setNamedItem(attribute);"],
  [["eval"], "(0, eval)(code);"],
  [["constructor access", "constructor access"], "({}).constructor.constructor(code)();"],
  [["string timer"], "setTimeout(code, 0);"],
  [["string timer"], "setInterval(handler, 10);"],
  [["computed element name"], 'document.createElement("scr" + "ipt");'],
  [["computed element name"], "document.createElementNS(ns, name);"],
  [["URL property assignment"], "anchor.href += value;"],
  [["style sink"], "node.style.cssText = value;"],
  [["style sink"], "node.style = value;"],
  [["style sink"], "sheet.insertRule(rule);"],
  [["style sink"], "document.adoptedStyleSheets = [sheet];"],
] as const) {
  test(`the scan finds ${sinks.join(" and ")} in: ${source}`, () => {
    assert.deepEqual(scanSource("example.ts", source).map((f) => f.sink), sinks);
  });
}

test("a sink split across lines is found, on the line where it starts", () => {
  assert.deepEqual(scanSource("example.ts", "const a = 1;\nnode.\n  innerHTML = value;\n").map((f) => [f.sink, f.line]), [["innerHTML", 2]]);
  assert.deepEqual(scanSource("example.ts", "const a = 1;\nnode\n  .innerHTML = value;\n").map((f) => [f.sink, f.line]), [["innerHTML", 3]]);
  assert.deepEqual(scanSource("example.ts", "document\n  .write(value);\n").map((f) => f.sink), ["document.write"]);
});

test("ordinary code is not flagged", () => {
  for (const source of ["setTimeout(() => tick(), 0);", "setTimeout(async () => tick(), 0);", "setTimeout(function () { tick(); }, 0);",
    'document.createElement("div");', 'document.createElementNS(SVG, "rect");', "class Session { constructor(graph) {} }",
    "const evaluation = check(value);", "node.textContent = value;", "const styles = sheets.length;"]) {
    assert.deepEqual(scanSource("example.ts", source), [], source);
  }
});

test("member names built from strings and computed Reflect access are found", () => {
  assert.deepEqual(scanSource("example.ts", 'node["inner" + "HTML"] = value;').map((f) => f.sink), ["computed member from text"]);
  assert.deepEqual(scanSource("example.ts", "node[prefix + 'HTML'] = value;").map((f) => f.sink), ["computed member from text"]);
  assert.deepEqual(scanSource("example.ts", 'Reflect["set"](node, name, value);').map((f) => f.sink), ["reflective write"]);
});
