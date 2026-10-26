import assert from "node:assert/strict";
import { test } from "node:test";
import { DebugObserver } from "../src/debug-observer.ts";

test("records the observed stop and only the location fields actually reported", () => {
  const observer = new DebugObserver("debug-a");
  observer.send({ type: "event", event: "stopped", body: { reason: "breakpoint", threadId: 7, allThreadsStopped: true } });
  observer.receive({ type: "request", seq: 4, command: "stackTrace", arguments: { threadId: 7 } });
  observer.send({ type: "response", request_seq: 4, command: "stackTrace", success: true, body: { stackFrames: [
    { id: 91, name: "Run", source: { name: "CounterDemo.cs", path: "/workspace/CounterDemo.cs", sourceReference: 0 }, line: 8, column: 9, endLine: 8, endColumn: 16 },
    { id: 92, name: "Caller", line: 2 },
  ] } });
  assert.deepEqual(observer.stopped, { session: "debug-a", pause: 1, reason: "breakpoint", threadId: 7, allThreadsStopped: true });
  assert.equal(observer.frames[0]?.source?.path, "/workspace/CounterDemo.cs");
  assert.deepEqual([observer.frames[0]?.line, observer.frames[0]?.column, observer.frames[0]?.endLine, observer.frames[0]?.endColumn], [8, 9, 8, 16]);
  assert.equal(observer.frames[1]?.column, undefined);
  assert.equal(observer.frames[1]?.endLine, undefined);
  assert.equal(observer.frames[1]?.source, undefined);
  assert.equal(observer.frames[0]?.lineBase, 1);
});
test("the initialize request determines coordinate bases without guessed conversions", () => {
  const observer = new DebugObserver("debug-a");
  observer.receive({ type: "request", command: "initialize", arguments: { linesStartAt1: false, columnsStartAt1: false } });
  observer.send({ type: "event", event: "stopped" });
  observer.receive({ type: "request", seq: 1, command: "stackTrace" });
  observer.send({ type: "response", request_seq: 1, command: "stackTrace", success: true, body: { stackFrames: [{ id: 1, name: "Run", line: 0, column: 0 }] } });
  assert.equal(observer.frames[0]?.line, 0);
  assert.equal(observer.frames[0]?.lineBase, 0);
  assert.equal(observer.frames[0]?.columnBase, 0);
});
test("resume, stepping, new pauses and session exit retire old frame replies", () => {
  for (const action of ["continued", "terminated", "exited", "next", "stopped"]) {
    const observer = new DebugObserver("debug-a");
    observer.send({ type: "event", event: "stopped" });
    observer.receive({ type: "request", seq: 1, command: "stackTrace" });
    if (action === "next") observer.receive({ type: "request", command: "next" });
    else observer.send({ type: "event", event: action });
    observer.send({ type: "response", request_seq: 1, command: "stackTrace", success: true,
      body: { stackFrames: [{ id: 1, name: "Obsolete", line: 9 }] } });
    assert.deepEqual(observer.frames, [], action);
    if (action !== "stopped") assert.equal(observer.stopped, undefined);
    else assert.equal(observer.stopped?.pause, 2);
  }
});
test("failed or unsolicited stack replies add no frames and sessions remain separate", () => {
  const a = new DebugObserver("debug-a");
  const b = new DebugObserver("debug-b");
  a.send({ type: "event", event: "stopped", body: { reason: "exception" } });
  a.receive({ type: "request", seq: 2, command: "stackTrace" });
  a.send({ type: "response", request_seq: 2, command: "stackTrace", success: false });
  a.send({ type: "response", request_seq: 90, command: "stackTrace", success: true,
    body: { stackFrames: [{ id: 1, name: "Unsolicited" }] } });
  assert.deepEqual(a.frames, []);
  assert.equal(b.stopped, undefined);
  assert.deepEqual(b.frames, []);
});
