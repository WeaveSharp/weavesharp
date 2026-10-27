import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { test } from "node:test";
import { contentSecurityPolicy, surfacePage } from "../../../src/surface/src/isolation/index.ts";
import { scanSource } from "../../../src/surface/src/isolation/sinks.ts";
import { FakeDocument, type FakeElement, shownText } from "../../../src/surface/test/support.ts";
import { render } from "../../canvas/src/render.ts";
import { projectStandIn } from "../../vscode/src/stand-in.ts";
import { schemas } from "../../vscode/src/schemas.generated.ts";
import { browserFiles, buildPage, noncePlaceholder, origin, pageOptions } from "../webview/page.ts";
import { receiveBase64 } from "../webview/receive.ts";
import { commitControl, diagnosticList, editNotice, RiderSurface } from "../webview/client.ts";

const base64 = (text: string): string => Buffer.from(text, "utf8").toString("base64");
function publication(source = "class Meter { static void Tick() { int count = 4; count++; return; } }", revision = 1,
  hostVersion = "9007199254740999", documentUri = "file:///project/Meter.cs") {
  const context = { workspaceId: "rider-view-1", projectId: "stand-in", compilationContextId: "no-compiler",
    snapshot: { hostId: "rider", documentUri, hostVersion, snapshotId: `rider-view-1-${revision}` } };
  return { version: { major: 0, minor: 0 }, family: "publish-graph", kind: "notification", requestId: `publication-${revision}`,
    payload: { ...projectStandIn(source, context, revision).graph, mode: "complete" }, extensions: {} };
}

test("the build uses SEC-04's page and corresponding header policy", () => {
  const built = buildPage();
  assert.equal(built.html, surfacePage(pageOptions));
  assert.equal(built.policy, contentSecurityPolicy(pageOptions) + "; frame-ancestors 'none'");
  assert.deepEqual(pageOptions.resourceOrigins, [origin]);
  const escapedPolicy = /http-equiv="Content-Security-Policy" content="([^"]+)"/.exec(built.html)?.[1];
  assert.ok(escapedPolicy);
  const metaPolicy = escapedPolicy.replace(/&#([0-9]+);/g, (_, number: string) => String.fromCharCode(Number(number)));
  assert.equal(built.policy, metaPolicy + "; frame-ancestors 'none'");
  assert.equal(Buffer.from(noncePlaceholder, "base64").length, 16);
  assert.equal(noncePlaceholder.length, 24);
  assert.equal(built.html.split(noncePlaceholder).length - 1, 4);
  assert.equal(built.policy.split(noncePlaceholder).length - 1, 2);
  assert.match(built.html, new RegExp(`nonce="${noncePlaceholder.replace(/[+]/g, "\\+")}"`));
});

test("generated resources include exactly the browser module closure and CSS", () => {
  const directory = resolve("experiments/rider/out/resources/surface");
  const inventory = readFileSync(join(directory, "files.txt"), "utf8").trim().split("\n");
  assert.deepEqual(inventory, ["index.html", ...browserFiles]);
  assert.equal(readFileSync(join(directory, "index.html"), "utf8"), buildPage().html);
  assert.equal(readFileSync(join(directory, "page-policy.txt"), "utf8"), buildPage().policy);
  assert.equal(readFileSync(join(directory, "experiments/canvas/src/canvas.css"), "utf8"),
    readFileSync("experiments/canvas/src/canvas.css", "utf8"));
  const reached = new Set<string>();
  function visit(module: string): void {
    if (reached.has(module)) return;
    reached.add(module);
    assert.ok(browserFiles.includes(module), `Unpackaged import: ${module}`);
    const text = readFileSync(join(directory, module), "utf8");
    for (const match of text.matchAll(/(?:from|import)\s*["'](\.[^"']+)["']/g)) {
      visit(resolve(directory, dirname(module), match[1]!).slice(directory.length + 1).replaceAll("\\", "/"));
    }
  }
  visit("experiments/rider/webview/entry.js");
  assert.deepEqual([...reached].sort(), browserFiles.filter((name) => name.endsWith(".js")).sort());
});

test("shared generated schemas retain the protocol's exact contents", () => {
  for (const name of readdirSync("protocol/v0")) {
    assert.deepEqual(schemas.get(name), JSON.parse(readFileSync(join("protocol/v0", name), "utf8")));
  }
});

test("Base64 preserves Unicode and passes a valid publication to readInbound", () => {
  const message = publication(undefined, 1, "1", "file:///project/測定.cs");
  const raw = JSON.stringify(message);
  const incoming = receiveBase64(base64(raw));
  assert.equal(incoming.text, raw);
  assert.equal(incoming.result.accepted, true);
});

test("decoding refuses malformed Base64 and invalid UTF-8", () => {
  for (const encoded of ["!not-base64!", "a", "====", "_w==", "/w==", "wK8=", "7aCA", "8CiMKA=="]) {
    assert.equal(receiveBase64(encoded).result.accepted, false, encoded);
  }
});

test("SEC-04 refuses bad JSON, wrong families, prototype keys and broken schemas", () => {
  const valid = publication();
  for (const raw of ["not JSON", JSON.stringify({ ...valid, family: "prepare-edit", kind: "query" }),
    JSON.stringify({ ...valid, payload: {} }), JSON.stringify(valid).replace('"extensions":{}', '"extensions":{"__proto__":{}}')]) {
    assert.equal(receiveBase64(base64(raw)).result.accepted, false);
  }
});

test("source-like strings arrive as text, including quotes and line separators", () => {
  const message = publication("not a sample");
  const text = '</script>"\\\u2028\u2029 שלום 測定';
  message.payload = { ...message.payload, diagnostics: [{ ...message.payload.diagnostics[0]!, message: text }] };
  const incoming = receiveBase64(base64(JSON.stringify(message)));
  assert.equal(incoming.result.accepted, true);
  const surface = new RiderSurface();
  assert.equal(surface.receive(base64(JSON.stringify(message))), true);
  const fake = new FakeDocument();
  const views = render(fake, surface.session!.graph);
  assert.ok(shownText(views.outline as FakeElement).length > 0);
  const diagnostics = diagnosticList(fake, surface.session!.graph) as FakeElement;
  assert.ok(shownText(diagnostics).includes('</script>"'));
  assert.ok(shownText(diagnostics).includes("שלום 測定"));
  assert.equal(fake.created.some((node) => node.kind === "element" && node.name === "script"), false);
  assert.equal(surface.session!.graph.diagnostics[0]!.message, text);
});

test("native edit and undo publications refresh the view even if host stamps decrease", () => {
  const client = new RiderSurface();
  const source = "class Meter { void Tick() { int count = 4; count++; } }";
  assert.equal(client.receive(base64(JSON.stringify(publication(source, 1, "19")))), true);
  client.session!.move("next");
  assert.equal(client.session!.graph.nodes.length, 2);
  assert.equal(client.receive(base64(JSON.stringify(publication(source.replace("count++;", "; count++;"), 2, "20")))), true);
  assert.equal(client.session!.graph.nodes.length, 3);
  assert.equal(client.receive(base64(JSON.stringify(publication(source, 3, "19")))), true);
  assert.equal(client.session!.graph.nodes.length, 2);
  assert.equal(client.receive(base64(JSON.stringify(publication(source, 2, "20")))), false);
  assert.equal(client.session!.graph.revision, 3);
});

test("commit is inert and its visible control is disabled", () => {
  const client = new RiderSurface();
  client.receive(base64(JSON.stringify(publication())));
  const graph = client.session!.graph;
  assert.equal(client.commit(), false);
  assert.equal(client.notice, editNotice);
  assert.equal(client.session!.graph, graph);
  const control = commitControl(new FakeDocument()) as FakeElement;
  assert.equal(control.attributes.get("disabled"), "disabled");
  assert.equal(control.attributes.get("aria-describedby"), "editing-note");
  assert.match(shownText(control), /unavailable/);
});

test("SEC-04 reports no sinks anywhere in the Rider webview source tree", () => {
  const files: string[] = [];
  function collect(directory: string): void {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) collect(path);
      else files.push(path);
    }
  }
  collect("experiments/rider/webview");
  assert.ok(files.some((file) => file.endsWith("generate.ts")));
  assert.ok(files.some((file) => file.endsWith("entry.ts")));
  assert.deepEqual(files.flatMap((file) => scanSource(file, readFileSync(file, "utf8"))), []);
});
