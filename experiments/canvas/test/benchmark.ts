import { performance } from "node:perf_hooks";
import { arch, platform, release } from "node:os";
import { render } from "../src/render.ts";
import { FakeDocument, elements } from "./support.ts";
import { chain } from "./fixtures.ts";

const results = [];
for (const count of [10, 100, 1000]) {
  const graph = chain(count);
  for (let warmup = 0; warmup < 3; warmup++) render(new FakeDocument(), graph);
  const elapsed: number[] = [];
  let counts = { canvas: 0, outline: 0, svg: 0, total: 0, text: 0 };
  for (let run = 0; run < 9; run++) {
    const document = new FakeDocument();
    const start = performance.now();
    const views = render(document, graph);
    elapsed.push(performance.now() - start);
    const canvas = elements(views.canvas);
    const outline = elements(views.outline);
    counts = { canvas: canvas.length, outline: outline.length,
      svg: canvas.filter((node) => node.namespace === "http://www.w3.org/2000/svg").length,
      total: document.created.filter((node) => node.kind === "element").length,
      text: document.created.filter((node) => node.kind === "text").length };
  }
  elapsed.sort((a, b) => a - b);
  results.push({ nodes: count, ...counts, medianMs: Number(elapsed[4]!.toFixed(3)), minMs: Number(elapsed[0]!.toFixed(3)), maxMs: Number(elapsed[8]!.toFixed(3)) });
}
console.log(JSON.stringify({ runtime: process.version, platform: platform(), release: release(), arch: arch(), warmups: 3, samples: 9, results }, null, 2));
