/**
 * The source rule behind the DOM builder: the graph surface's code never uses a
 * sink that parses a string as HTML, runs a string as code, or navigates. The
 * scan reads source text, so it catches a sink before any test exercises it.
 * This file is the one file the scan skips, because it has to name the sinks.
 */

export interface SinkFinding {
  readonly file: string;
  readonly line: number;
  readonly sink: string;
}

const SINKS: ReadonlyArray<readonly [string, RegExp]> = [
  ["innerHTML", /\.innerHTML\b/],
  ["outerHTML", /\.outerHTML\b/],
  ["insertAdjacentHTML", /\binsertAdjacentHTML\b/],
  ["document.write", /\bdocument\s*\.\s*write(ln)?\b/],
  ["eval", /\beval\s*\(/],
  ["Function constructor", /\bnew\s+Function\b|\bFunction\s*\(/],
  ["string timer", /\bset(Timeout|Interval)\s*\(\s*["'`]/],
  ["srcdoc", /\bsrcdoc\b/],
  ["DOMParser", /\bDOMParser\b/],
  ["createContextualFragment", /\bcreateContextualFragment\b/],
  ["URL property assignment", /\.(href|src|action|formAction|data)\s*=[^=]/],
  ["window.open", /\bwindow\s*\.\s*open\s*\(/],
  ["navigation", /\blocation\s*(\.\s*(href|assign|replace)\b|=[^=])/],
  ["importScripts", /\bimportScripts\b/],
  ["dynamic import", /\bimport\s*\(/],
  ["setAttribute outside the DOM builder", /\.setAttribute\s*\(/],
  ["style or script element", /createElement(NS)?\s*\([^)]*["'](script|style|iframe|object|embed|link|meta|base)["']/],
];

/** The files allowed to call setAttribute: the DOM builder checks every attribute first. */
const SET_ATTRIBUTE_ALLOWED = new Set(["dom.ts"]);

export function scanSource(file: string, text: string): SinkFinding[] {
  const name = file.split(/[\\/]/).pop() ?? file;
  const findings: SinkFinding[] = [];
  text.split("\n").forEach((line, index) => {
    for (const [sink, pattern] of SINKS) {
      if (sink === "setAttribute outside the DOM builder" && SET_ATTRIBUTE_ALLOWED.has(name)) {
        continue;
      }
      if (pattern.test(line)) {
        findings.push({ file, line: index + 1, sink });
      }
    }
  });
  return findings;
}
