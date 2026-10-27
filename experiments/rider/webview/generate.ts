import { copyFileSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { browserFiles, buildPage } from "./page.ts";

// Invoked from the repository root after the single Rider TypeScript build.
const output = resolve("experiments/rider/out");
const resources = resolve(output, "resources/surface");
mkdirSync(resources, { recursive: true });
const page = buildPage();
writeFileSync(resolve(resources, "index.html"), page.html);
writeFileSync(resolve(resources, "page-policy.txt"), page.policy);
writeFileSync(resolve(resources, "files.txt"), ["index.html", ...browserFiles].join("\n") + "\n");
for (const name of browserFiles) {
  const target = resolve(resources, name);
  mkdirSync(dirname(target), { recursive: true });
  copyFileSync(resolve(name.endsWith(".css") ? "." : output, name), target);
}
