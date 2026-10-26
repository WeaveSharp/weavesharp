const { copyFileSync, mkdirSync, writeFileSync } = require("node:fs");
const { resolve, join } = require("node:path");

const destination = join(__dirname, "out/webview/experiments/canvas/src");
mkdirSync(destination, { recursive: true });
copyFileSync(resolve(__dirname, "../canvas/src/canvas.css"), join(destination, "canvas.css"));

// The shared sources belong to the repository's ES-module package. Preserve
// that format in the emitted tree beside the CommonJS extension entry.
for (const directory of ["out/host/src", "out/host/experiments/canvas"]) {
  const path = join(__dirname, directory);
  mkdirSync(path, { recursive: true });
  writeFileSync(join(path, "package.json"), '{"type":"module"}\n');
}
