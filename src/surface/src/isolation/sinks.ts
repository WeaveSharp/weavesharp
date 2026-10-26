/**
 * The source rule behind the DOM builder: the graph surface's code never uses a
 * sink that parses a string as HTML, runs a string as code, navigates, or writes
 * styles. The scan reads the whole source text, so a sink split across lines or
 * reached through a computed member is found before any test exercises it. It is
 * a backstop, not a proof: the page's Content-Security-Policy and Trusted Types
 * refuse these sinks at run time. This file is the one file the scan skips,
 * because it has to name the sinks.
 */

export interface SinkFinding {
  readonly file: string;
  readonly line: number;
  readonly sink: string;
}

/** Sink names reached through a computed member, for example node["innerHTML"]. */
const COMPUTED = "innerHTML|outerHTML|insertAdjacentHTML|srcdoc|write|writeln|setAttribute|setAttributeNS|setAttributeNode|cssText|href|src|action|formAction|eval|constructor|Function";

const SINKS: ReadonlyArray<readonly [string, RegExp]> = [
  ["innerHTML", /\.\s*innerHTML\b/g],
  ["outerHTML", /\.\s*outerHTML\b/g],
  ["insertAdjacentHTML", /\binsertAdjacentHTML\b/g],
  ["document.write", /\bdocument\s*\.\s*write(ln)?\b/g],
  ["eval", /\beval\b/g],
  ["Function constructor", /\bnew\s+Function\b|\bFunction\s*\(/g],
  ["constructor access", /\.\s*constructor\b/g],
  // A timer's first argument must be a function written in place, never a value that could be a string.
  ["string timer", /\bset(Timeout|Interval)\s*\((?!\s*(\(|async\b|function\b))/g],
  ["srcdoc", /\bsrcdoc\b/g],
  ["DOMParser", /\bDOMParser\b/g],
  ["createContextualFragment", /\bcreateContextualFragment\b/g],
  ["URL property assignment", /\.\s*(href|src|action|formAction|data|baseURI)\s*\+?=(?!=)/g],
  ["window.open", /\bwindow\s*\.\s*open\s*\(/g],
  ["navigation", /\blocation\s*(\.\s*(href|assign|replace)\b|\+?=(?!=))/g],
  ["importScripts", /\bimportScripts\b/g],
  ["dynamic import", /\bimport\s*\(/g],
  ["setAttribute outside the DOM builder", /\.\s*setAttribute(NS|Node|NodeNS)?\s*\(|\bsetNamedItem(NS)?\s*\(/g],
  ["style or script element", /createElement(NS)?\s*\([^)]*["'`](script|style|iframe|frame|object|embed|link|meta|base|form)["'`]/g],
  ["computed element name", /\bcreateElement\s*\((?!\s*["'`][A-Za-z][A-Za-z0-9-]*["'`]\s*[,)])|\bcreateElementNS\s*\([^,()]*,(?!\s*["'`][A-Za-z][A-Za-z0-9-]*["'`]\s*[,)])/g],
  ["computed sink member", new RegExp(`\\[\\s*["'\`](${COMPUTED})["'\`]\\s*\\]`, "g")],
  ["reflective write", /\bReflect\s*\.\s*(set|apply|construct|defineProperty)\b|\bObject\s*\.\s*(assign|defineProperty|defineProperties)\s*\(/g],
  ["style sink", /\.\s*cssText\b|\binsertRule\b|\breplaceSync\b|\badoptedStyleSheets\b|\bnew\s+CSSStyleSheet\b|\.\s*style\s*(\.\s*setProperty\b|=(?!=)|\[)/g],
];

/** The one file allowed to set attributes and create elements by name: the DOM builder checks both first. */
const BUILDER = "src/surface/src/isolation/dom.ts";
const BUILDER_RULES: ReadonlySet<string> = new Set(["setAttribute outside the DOM builder", "computed element name"]);

function isBuilder(file: string): boolean {
  const path = file.replaceAll("\\", "/");
  return path === BUILDER || path.endsWith(`/${BUILDER}`);
}

export function scanSource(file: string, text: string): SinkFinding[] {
  const builder = isBuilder(file);
  const lineStarts = [0];
  for (let index = text.indexOf("\n"); index !== -1; index = text.indexOf("\n", index + 1)) {
    lineStarts.push(index + 1);
  }
  const lineOf = (offset: number): number => {
    let low = 0;
    let high = lineStarts.length - 1;
    while (low < high) {
      const middle = Math.ceil((low + high) / 2);
      if ((lineStarts[middle] ?? 0) <= offset) low = middle; else high = middle - 1;
    }
    return low + 1;
  };
  const findings: SinkFinding[] = [];
  for (const [sink, pattern] of SINKS) {
    if (builder && BUILDER_RULES.has(sink)) {
      continue;
    }
    for (const match of text.matchAll(pattern)) {
      findings.push({ file, line: lineOf(match.index), sink });
    }
  }
  return findings.sort((a, b) => a.line - b.line || a.sink.localeCompare(b.sink));
}
