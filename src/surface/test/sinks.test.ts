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

test("setAttribute is allowed only in the DOM builder", () => {
  assert.deepEqual(scanSource("src/isolation/dom.ts", "created.setAttribute(attribute, value);"), []);
});
