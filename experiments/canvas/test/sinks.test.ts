import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { scanSource } from "../../../src/surface/src/isolation/sinks.ts";
import { repositoryRoot } from "./support.ts";

test("SEC-04 sink scan reports no finding in the experiment source", () => {
  const source = join(repositoryRoot, "experiments", "canvas", "src");
  const walk = (folder: string): string[] => readdirSync(folder, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? walk(join(folder, entry.name)) : [join(folder, entry.name)]);
  const files = walk(source);
  assert.ok(files.length >= 5);
  assert.deepEqual(files.flatMap((file) => scanSource(file, readFileSync(file, "utf8"))), []);
});
