import { test } from "node:test";
import assert from "node:assert/strict";
import { readInbound, writeOutbound, readFromSurface, writeToSurface, nestingDepth, unsupportedKeywords, INBOUND, OUTBOUND, IsolationError } from "../src/isolation/index.ts";
import { loadProtocolSchemas, protocolExamples } from "./support.ts";

const schemas = loadProtocolSchemas();
const examples = protocolExamples();
const valid = (family: string) => examples.filter((e) => e.family === family && e.name.startsWith("valid"));
const firstValid = (family: string, kind?: string) =>
  valid(family).map((e) => JSON.parse(e.text) as Record<string, unknown>).find((m) => kind === undefined || m["kind"] === kind);

test("the validator implements every keyword the protocol schemas use", () => {
  assert.deepEqual(unsupportedKeywords(schemas), []);
  assert.equal(schemas.size, 11);
});

test("every valid protocol example the surface may receive is accepted", () => {
  let accepted = 0;
  for (const example of examples.filter((e) => e.name.startsWith("valid"))) {
    const message = JSON.parse(example.text) as Record<string, unknown>;
    const inbound = (INBOUND[example.family] ?? []).includes(message["kind"] as string);
    const result = readInbound(example.text, schemas);
    assert.equal(result.accepted, inbound, `${example.family}/${example.name}`);
    if (inbound) accepted++;
  }
  assert.ok(accepted >= 5);
});

test("every invalid protocol example is refused", () => {
  for (const example of examples.filter((e) => e.name.startsWith("invalid"))) {
    assert.equal(readInbound(example.text, schemas).accepted, false, `${example.family}/${example.name}`);
  }
});

test("an accepted message is frozen", () => {
  const message = firstValid("publish-graph");
  const result = readInbound(JSON.stringify(message), schemas);
  assert.ok(result.accepted);
  if (result.accepted) {
    assert.ok(Object.isFrozen(result.message));
    assert.ok(Object.isFrozen(result.message["payload"]));
  }
});

test("a message that is not text is refused", () => {
  assert.equal(readInbound(firstValid("publish-graph"), schemas).accepted, false);
});

test("an oversized message is refused before it is parsed", () => {
  const huge = `{"family":"publish-graph","pad":"${"a".repeat(2000)}"}`;
  const result = readInbound(huge, schemas, { maxCharacters: 1000, maxDepth: 32 });
  assert.deepEqual(result, { accepted: false, reason: "The message is longer than the limit." });
});

test("an over-deep message is refused before it is parsed", () => {
  const deep = "[".repeat(100000) + "]".repeat(100000);
  assert.equal(nestingDepth(deep), 100000);
  assert.deepEqual(readInbound(deep, schemas), { accepted: false, reason: "The message nests deeper than the limit." });
});

test("nesting inside strings does not count", () => {
  assert.equal(nestingDepth('{"a":"[[[[{{{{\\"]]]"}'), 1);
});

test("malformed JSON is refused", () => {
  assert.deepEqual(readInbound("{not json", schemas), { accepted: false, reason: "The message is not valid JSON." });
});

for (const key of ["__proto__", "constructor", "prototype"]) {
  test(`a ${key} key anywhere is refused`, () => {
    const message = firstValid("publish-graph") as Record<string, unknown>;
    const text = JSON.stringify(message).replace('"payload":{', `"payload":{"${key}":{"polluted":true},`);
    assert.deepEqual(readInbound(text, schemas), { accepted: false, reason: "The message carries a forbidden key." });
  });
}

test("a family the surface does not receive is refused", () => {
  const message = { ...firstValid("prepare-edit") };
  assert.deepEqual(readInbound(JSON.stringify(message), schemas), { accepted: false, reason: "The surface does not accept that family and kind." });
  assert.equal(readInbound(JSON.stringify({ ...firstValid("publish-graph"), family: "../../etc/passwd" }), schemas).accepted, false);
});

test("a kind the surface does not receive for its family is refused", () => {
  const message = { ...firstValid("edit-result", "response"), kind: "mutation" };
  assert.deepEqual(readInbound(JSON.stringify(message), schemas), { accepted: false, reason: "The surface does not accept that family and kind." });
});

test("schema violations are refused by rule without echoing content", () => {
  const base = firstValid("publish-graph") as Record<string, unknown>;
  const cases: [string, Record<string, unknown>][] = [
    ["an unknown field", { ...base, injected: "<script>" }],
    ["a missing field", Object.fromEntries(Object.entries(base).filter(([k]) => k !== "payload"))],
    ["a wrong type", { ...base, requestId: 42 }],
    ["an over-long string", { ...base, requestId: "x".repeat(10000) }],
  ];
  for (const [name, message] of cases) {
    const result = readInbound(JSON.stringify(message), schemas);
    assert.equal(result.accepted, false, name);
    if (!result.accepted) {
      assert.match(result.reason, /^The message breaks its schema at /, name);
      assert.doesNotMatch(result.reason, /script|xxxx/, name);
    }
  }
});

test("allowed commands are written; anything else is refused", () => {
  for (const [family, kinds] of Object.entries(OUTBOUND)) {
    for (const kind of kinds) {
      const message = firstValid(family, kind);
      if (message !== undefined) {
        assert.equal(typeof writeOutbound(message, schemas), "string", `${family} ${kind}`);
      }
    }
  }
  assert.throws(() => writeOutbound(firstValid("publish-graph"), schemas), IsolationError);
  assert.throws(() => writeOutbound({ ...firstValid("request-graph"), kind: "mutation" }, schemas), IsolationError);
  assert.throws(() => writeOutbound({ ...firstValid("request-graph"), extra: true }, schemas), IsolationError);
  assert.throws(() => writeOutbound({ family: "run-shell", kind: "mutation" }, schemas), IsolationError);
});

test("a host reads exactly the surface's commands, with the same checks", () => {
  let read = 0;
  for (const example of examples.filter((e) => e.name.startsWith("valid"))) {
    const message = JSON.parse(example.text) as Record<string, unknown>;
    const command = (OUTBOUND[example.family] ?? []).includes(message["kind"] as string);
    const result = readFromSurface(example.text, schemas);
    assert.equal(result.accepted, command, `${example.family}/${example.name}`);
    if (result.accepted) {
      assert.ok(Object.isFrozen(result.message));
      read++;
    }
  }
  assert.ok(read >= 3);
  for (const example of examples.filter((e) => e.name.startsWith("invalid"))) {
    assert.equal(readFromSurface(example.text, schemas).accepted, false, `${example.family}/${example.name}`);
  }
});

test("a host refuses what the surface may not send, before or without parsing it", () => {
  const publication = JSON.stringify(firstValid("publish-graph"));
  assert.deepEqual(readFromSurface(publication, schemas), { accepted: false, reason: "The host does not accept that family and kind from the surface." });
  assert.deepEqual(readFromSurface(firstValid("request-graph"), schemas), { accepted: false, reason: "Messages arrive as JSON text." });
  const command = JSON.stringify(firstValid("request-graph"));
  assert.deepEqual(readFromSurface(command, schemas, { maxCharacters: 10, maxDepth: 32 }), { accepted: false, reason: "The message is longer than the limit." });
  assert.deepEqual(readFromSurface("[".repeat(100) + "]".repeat(100), schemas), { accepted: false, reason: "The message nests deeper than the limit." });
  const poisoned = command.replace("{", '{"__proto__":{"polluted":true},');
  assert.deepEqual(readFromSurface(poisoned, schemas), { accepted: false, reason: "The message carries a forbidden key." });
  assert.equal(({} as Record<string, unknown>)["polluted"], undefined);
  const escaped = command.replace("{", '{"\\u005f_proto__":{"polluted":true},');
  assert.equal(readFromSurface(escaped, schemas).accepted, false);
  const broken = readFromSurface(JSON.stringify({ ...firstValid("request-graph"), extra: "<script>" }), schemas);
  assert.equal(broken.accepted, false);
  if (!broken.accepted) {
    assert.doesNotMatch(broken.reason, /script/);
  }
});

test("a host writes only what the surface accepts, schema-valid", () => {
  for (const [family, kinds] of Object.entries(INBOUND)) {
    for (const kind of kinds) {
      const message = firstValid(family, kind);
      if (message !== undefined) {
        const text = writeToSurface(message, schemas);
        assert.equal(readInbound(text, schemas).accepted, true, `${family} ${kind}`);
      }
    }
  }
  assert.throws(() => writeToSurface(firstValid("request-graph"), schemas), IsolationError);
  assert.throws(() => writeToSurface({ ...firstValid("publish-graph"), extra: true }, schemas), IsolationError);
  assert.throws(() => writeToSurface(JSON.parse('{"family":"publish-graph","kind":"notification","__proto__":{}}'), schemas), IsolationError);
  assert.throws(() => writeToSurface(firstValid("publish-graph"), schemas, { maxCharacters: 10, maxDepth: 32 }), IsolationError);
});
