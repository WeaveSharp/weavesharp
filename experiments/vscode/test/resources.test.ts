import assert from "node:assert/strict";
import { test } from "node:test";
import { readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { scanSource } from "../../../src/surface/src/isolation/sinks.ts";
import { surfacePage } from "../../../src/surface/src/isolation/index.ts";
import { pageFor } from "../src/page.ts";

const spike = resolve("experiments/vscode");
test("the generated schema module exactly matches the protocol files", () => {
  const generator = require(join(spike, "generate-schemas.cjs")) as { schemaModule(): string };
  assert.equal(readFileSync(join(spike, "src/schemas.generated.ts"), "utf8"), generator.schemaModule());
});
test("SEC-04's sink scan covers every source file including the generated module", () => {
  const files: string[] = [];
  function visit(directory: string): void {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) visit(path);
      else files.push(path);
    }
  }
  visit(join(spike, "src"));
  assert.ok(files.some((file) => file.endsWith("webview.ts")));
  assert.ok(files.some((file) => file.endsWith("schemas.generated.ts")));
  assert.deepEqual(files.flatMap((file) => scanSource(file, readFileSync(file, "utf8"))), []);
});
test("page generation delegates to surfacePage and asks for a fresh nonce", () => {
  const script = "https://file+.vscode-resource.vscode-cdn.net/work/webview.js";
  const style = "https://file+.vscode-resource.vscode-cdn.net/work/canvas.css";
  const first = pageFor(script, style);
  const second = pageFor(script, style);
  assert.notEqual(first.options.nonce, second.options.nonce);
  assert.equal(first.html, surfacePage(first.options));
  assert.equal(second.html, surfacePage(second.options));
  assert.deepEqual(first.options.resourceOrigins, ["https://*.vscode-cdn.net"]);
  assert.deepEqual(first.options.scripts, [script]);
  assert.deepEqual(first.options.styles, [style]);
});
test("the private manifest and dependency-free lock agree", () => {
  const manifest = JSON.parse(readFileSync(join(spike, "package.json"), "utf8"));
  const lock = JSON.parse(readFileSync(join(spike, "package-lock.json"), "utf8"));
  assert.equal(manifest.private, true);
  assert.equal(manifest.version, "0.0.0");
  assert.equal(manifest.type, "commonjs");
  assert.equal(manifest.engines.vscode, "^1.140.0");
  assert.equal(manifest.dependencies, undefined);
  assert.equal(manifest.scripts, undefined);
  assert.deepEqual(manifest.extensionKind, ["workspace"]);
  assert.equal(manifest.capabilities.virtualWorkspaces, false);
  assert.equal(manifest.capabilities.untrustedWorkspaces.supported, false);
  assert.deepEqual(lock.packages, { "": { name: manifest.name, version: manifest.version } });
  assert.equal(lock.lockfileVersion, 3);
  assert.equal(lock.requires, true);
});
