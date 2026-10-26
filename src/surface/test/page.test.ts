import { test } from "node:test";
import assert from "node:assert/strict";
import { surfacePage, resourceOriginAllows, PAGE_ROOT, IsolationError, type PageOptions } from "../src/isolation/index.ts";

const nonce = "AAAAAAAAAAAAAAAAAAAAAA==";
const origin = "https://*.vscode-cdn.net";
const base: PageOptions = {
  nonce,
  resourceOrigins: [origin],
  scripts: ["https://file+.vscode-resource.vscode-cdn.net/extension/out/webview/main.js"],
  styles: ["https://file+.vscode-resource.vscode-cdn.net/extension/out/webview/canvas.css"],
  title: "Graph",
};

test("the page puts its policy first and its nonce on every script and style", () => {
  const page = surfacePage(base);
  const head = page.slice(page.indexOf("<head>"), page.indexOf("</head>"));
  assert.ok(head.indexOf('http-equiv="Content-Security-Policy"') < head.indexOf("<link"));
  assert.match(page, /script-src &#39;nonce-AAAAAAAAAAAAAAAAAAAAAA==&#39; https:\/\/\*\.vscode-cdn\.net/);
  assert.match(page, /require-trusted-types-for &#39;script&#39;/);
  assert.equal(page.match(/<script /g)?.length, 1);
  assert.match(page, /<script type="module" src="https:\/\/file\+\.vscode-resource\.vscode-cdn\.net\/extension\/out\/webview\/main\.js" nonce="AAAAAAAAAAAAAAAAAAAAAA=="><\/script>/);
  assert.match(page, /<link rel="stylesheet" href="[^"]+canvas\.css" nonce="AAAAAAAAAAAAAAAAAAAAAA==">/);
  assert.match(page, new RegExp(`<div id="${PAGE_ROOT}"></div>`));
  assert.doesNotMatch(page, /<script>|\son[a-z]+=|\sstyle=/);
  assert.ok(page.indexOf('<meta charset="utf-8">') < 1024);
});

test("values are escaped where they land, so they cannot add markup", () => {
  const page = surfacePage({ ...base, title: `</title><script>alert(1)</script>"'&` });
  assert.match(page, /<title>&#60;\/title&#62;&#60;script&#62;alert\(1\)&#60;\/script&#62;&#34;&#39;&#38;<\/title>/);
  assert.equal(page.match(/<script/g)?.length, 1);
  const quoted = surfacePage({ ...base, scripts: [`https://a.vscode-cdn.net/x"><script>alert(1)</script>'.js`] });
  assert.equal(quoted.match(/<script/g)?.length, 1);
  assert.doesNotMatch(quoted, /"><script>/);
});

test("resources must be HTTPS URLs under a declared origin", () => {
  const refused = [
    "http://a.vscode-cdn.net/main.js",
    "data:text/javascript,alert(1)",
    "javascript:alert(1)",
    "//a.vscode-cdn.net/main.js",
    "main.js",
    "https://vscode-cdn.net/main.js",
    "https://a.vscode-cdn.net.example.com/main.js",
    "https://avscode-cdn.net/main.js",
    "https://user:pass@a.vscode-cdn.net/main.js",
    "https://a.vscode-cdn.net:8443/main.js",
    "https://a.vscode-cdn.net/main.js#fragment",
    "https://example.com/main.js",
  ];
  for (const url of refused) {
    assert.throws(() => surfacePage({ ...base, scripts: [url] }), IsolationError, url);
    assert.throws(() => surfacePage({ ...base, styles: [url] }), IsolationError, url);
  }
  assert.throws(() => surfacePage({ ...base, resourceOrigins: [], scripts: base.scripts }), IsolationError);
  assert.throws(() => surfacePage({ ...base, scripts: [] }), IsolationError);
});

test("titles, languages and nonces are checked", () => {
  assert.throws(() => surfacePage({ ...base, title: "" }), IsolationError);
  assert.throws(() => surfacePage({ ...base, title: "a\nb" }), IsolationError);
  assert.throws(() => surfacePage({ ...base, title: "x".repeat(257) }), IsolationError);
  assert.throws(() => surfacePage({ ...base, lang: 'en" onload="x' }), IsolationError);
  assert.throws(() => surfacePage({ ...base, nonce: "short" }), IsolationError);
  assert.match(surfacePage({ ...base, lang: "ar-EG" }), /<html lang="ar-EG">/);
});

test("origin matching follows CSP host-source rules", () => {
  assert.equal(resourceOriginAllows(origin, "https://file+.vscode-resource.vscode-cdn.net"), true);
  assert.equal(resourceOriginAllows(origin, "https://a.b.vscode-cdn.net"), true);
  assert.equal(resourceOriginAllows(origin, "https://vscode-cdn.net"), false);
  assert.equal(resourceOriginAllows(origin, "https://a.vscode-cdn.net:443"), false);
  assert.equal(resourceOriginAllows("https://*.example.com:8443", "https://a.example.com:8443"), true);
  assert.equal(resourceOriginAllows("https://*.example.com:8443", "https://a.example.com"), false);
  assert.equal(resourceOriginAllows("https://cdn.example.com", "https://cdn.example.com"), true);
  assert.equal(resourceOriginAllows("https://cdn.example.com", "https://a.cdn.example.com"), false);
  assert.equal(resourceOriginAllows("https://*", "https://anything"), false);
  assert.equal(resourceOriginAllows("*", "https://anything"), false);
});
