import { test } from "node:test";
import assert from "node:assert/strict";
import { contentSecurityPolicy, createNonce, IsolationError } from "../src/isolation/index.ts";

const nonce = "AAECAwQFBgcICQoLDA0ODw==";

test("the policy allows only the nonce and declared origins, and requires Trusted Types", () => {
  const policy = contentSecurityPolicy({ nonce, resourceOrigins: ["https://*.vscode-cdn.net"] });
  const directives = new Map(policy.split("; ").map((d) => [d.split(" ")[0], d.slice(d.indexOf(" ") + 1)]));
  assert.equal(directives.get("default-src"), "'none'");
  assert.equal(directives.get("script-src"), `'nonce-${nonce}' https://*.vscode-cdn.net`);
  assert.equal(directives.get("style-src"), `'nonce-${nonce}' https://*.vscode-cdn.net`);
  for (const name of ["connect-src", "media-src", "object-src", "frame-src", "child-src", "worker-src", "manifest-src", "base-uri", "form-action"]) {
    assert.equal(directives.get(name), "'none'", name);
  }
  assert.equal(directives.get("require-trusted-types-for"), "'script'");
  assert.equal(directives.get("trusted-types"), "'none'");
  assert.doesNotMatch(policy, /unsafe-|'self'|data:|blob:|(^|\s)\*(\s|;|$)/);
});

test("without resource origins, images and fonts are refused too", () => {
  const policy = contentSecurityPolicy({ nonce });
  assert.match(policy, /img-src 'none'/);
  assert.match(policy, /font-src 'none'/);
  assert.match(policy, new RegExp(`script-src 'nonce-${nonce.replace(/[+/=]/g, "\\$&")}';`));
});

for (const weak of ["", "short", "not base64 at all!!!!!!!", "AAAA"]) {
  test(`a weak or malformed nonce is refused: ${JSON.stringify(weak)}`, () => {
    assert.throws(() => contentSecurityPolicy({ nonce: weak }), IsolationError);
  });
}

for (const origin of ["*", "https:", "https://*", "http://example.com", "https://example.com/path", "'unsafe-inline'", "https://a.example 'unsafe-eval'", "data:", "https://*.*.example.com", "https://example.com; script-src *"]) {
  test(`an unsafe resource origin is refused: ${origin}`, () => {
    assert.throws(() => contentSecurityPolicy({ nonce, resourceOrigins: [origin] }), IsolationError);
  });
}

test("nonces come from the random source, 128 bits each", () => {
  const first = createNonce();
  const second = createNonce();
  assert.notEqual(first, second);
  assert.equal(atob(first).length, 16);
  assert.equal(createNonce((bytes) => bytes.fill(7)), btoa(String.fromCharCode(...new Array(16).fill(7))));
});
