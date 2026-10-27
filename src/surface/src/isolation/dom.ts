import { IsolationError } from "./errors.ts";
import { safeLabel, DEFAULT_LABEL_LENGTH } from "./text.ts";

/**
 * The only way the graph surface creates DOM. Elements come from an allow-list,
 * attributes from per-element allow-lists with checked values, and all text goes
 * into text nodes, so nothing the surface shows is ever parsed as HTML. Event
 * handlers, styles, URLs and frames are not expressible at all. The interfaces
 * are the small part of the DOM this module uses, so tests can supply a recording
 * document and the module can be checked without a browser.
 */

export interface NodeLike {
  readonly nodeType: number;
}

export interface ElementLike extends NodeLike {
  setAttribute(name: string, value: string): void;
  append(...nodes: NodeLike[]): void;
}

export interface DocumentLike {
  createElementNS(namespace: string, qualifiedName: string): ElementLike;
  createTextNode(data: string): NodeLike;
}

const HTML = "http://www.w3.org/1999/xhtml";
const SVG = "http://www.w3.org/2000/svg";

type Check = (value: string) => boolean;

const token = /^[a-z][a-z0-9-]{0,63}$/;
const classList: Check = (value) => value.split(" ").every((part) => token.test(part));
const identifier: Check = (value) => /^[A-Za-z0-9._:-]{1,128}$/.test(value);
const integer: Check = (value) => /^-?[0-9]{1,6}$/.test(value);
const number: Check = (value) => /^-?[0-9]{1,7}(\.[0-9]{1,4})?$/.test(value);
const anyText: Check = (value) => value.length <= 1024;
const oneOf = (...values: string[]): Check => (value) => values.includes(value);
const pathData: Check = (value) => value.length <= 16384 && /^[MLHVCSQTAZmlhvcsqtaz0-9 ,.\-]*$/.test(value);
const pointList: Check = (value) => value.length <= 16384 && /^[0-9 ,.\-]*$/.test(value);
const viewBox: Check = (value) => /^-?[0-9.]+ -?[0-9.]+ [0-9.]+ [0-9.]+$/.test(value) && value.length <= 64;
const transform: Check = (value) =>
  value.length <= 256 && /^((translate|scale|rotate)\([0-9 ,.\-]+\) ?)+$/.test(value);

/** Attributes every allowed element may carry, besides aria-* and data-ws-*. */
const GLOBAL: Readonly<Record<string, Check>> = {
  class: classList,
  id: identifier,
  role: (value) => token.test(value),
  tabindex: integer,
  dir: oneOf("auto", "ltr", "rtl"),
  hidden: oneOf("", "hidden"),
  title: anyText,
};

const geometry = (...names: string[]): Record<string, Check> =>
  Object.fromEntries(names.map((name) => [name, number]));

interface ElementRule {
  readonly namespace: string;
  readonly attributes: Readonly<Record<string, Check>>;
}

const ELEMENTS: Readonly<Record<string, ElementRule>> = {
  div: { namespace: HTML, attributes: {} },
  span: { namespace: HTML, attributes: {} },
  p: { namespace: HTML, attributes: {} },
  ul: { namespace: HTML, attributes: {} },
  ol: { namespace: HTML, attributes: {} },
  li: { namespace: HTML, attributes: {} },
  button: { namespace: HTML, attributes: { type: oneOf("button"), disabled: oneOf("", "disabled") } },
  svg: { namespace: SVG, attributes: { ...geometry("width", "height"), viewBox, focusable: oneOf("false") } },
  g: { namespace: SVG, attributes: { transform } },
  path: { namespace: SVG, attributes: { d: pathData, transform } },
  rect: { namespace: SVG, attributes: { ...geometry("x", "y", "width", "height", "rx", "ry"), transform } },
  line: { namespace: SVG, attributes: { ...geometry("x1", "y1", "x2", "y2"), transform } },
  polyline: { namespace: SVG, attributes: { points: pointList, transform } },
  circle: { namespace: SVG, attributes: { ...geometry("cx", "cy", "r"), transform } },
  text: { namespace: SVG, attributes: { ...geometry("x", "y", "dx", "dy"), transform } },
  title: { namespace: SVG, attributes: {} },
  desc: { namespace: SVG, attributes: {} },
};

const ARIA_NAME = /^aria-[a-z]{2,32}$/;

/** ARIA attributes that name other elements by id: they hold identifiers and nothing else. */
const ARIA_REFERENCES: ReadonlySet<string> = new Set([
  "aria-labelledby", "aria-describedby", "aria-controls", "aria-owns", "aria-activedescendant",
  "aria-details", "aria-errormessage", "aria-flowto",
]);
const identifierList: Check = (value) => value.length <= 1024 && value.split(" ").every((part) => identifier(part));

/**
 * Attributes whose text a tooltip shows or assistive technology may announce: the title and
 * every ARIA attribute that is not a list of references. They get a label's markers.
 */
function readable(attribute: string): boolean {
  return attribute === "title" || (ARIA_NAME.test(attribute) && !ARIA_REFERENCES.has(attribute));
}
const DATA_NAME = /^data-ws-[a-z][a-z0-9-]{0,31}$/;

function checkFor(rule: ElementRule, name: string): Check | undefined {
  if (Object.hasOwn(rule.attributes, name)) {
    return rule.attributes[name];
  }
  if (Object.hasOwn(GLOBAL, name)) {
    return GLOBAL[name];
  }
  if (ARIA_NAME.test(name)) {
    return ARIA_REFERENCES.has(name) ? identifierList : anyText;
  }
  if (DATA_NAME.test(name)) {
    return anyText;
  }
  return undefined;
}

export type Attributes = Readonly<Record<string, string>>;
export type Child = NodeLike | string;

/** Creates an allowed element with checked attributes; strings among the children become text nodes. */
export function element(document: DocumentLike, name: string, attributes: Attributes = {}, children: readonly Child[] = []): ElementLike {
  if (!Object.hasOwn(ELEMENTS, name)) {
    throw new IsolationError("The surface does not create that element.");
  }
  const rule = ELEMENTS[name] as ElementRule;
  const created = document.createElementNS(rule.namespace, name);
  for (const [attribute, given] of Object.entries(attributes)) {
    const check = checkFor(rule, attribute);
    if (check === undefined) {
      throw new IsolationError("The surface does not set that attribute on that element.");
    }
    if (typeof given !== "string") {
      throw new IsolationError("An attribute value failed its check.");
    }
    // Text people read or hear gets a label's treatment: bounded, hidden characters shown as markers.
    const value = readable(attribute) ? safeLabel(given).text : given;
    if (!readable(attribute) && !check(value)) {
      throw new IsolationError("An attribute value failed its check.");
    }
    created.setAttribute(attribute, value);
  }
  for (const child of children) {
    created.append(typeof child === "string" ? document.createTextNode(child) : child);
  }
  return created;
}

/** A text node: the only way any text reaches the DOM. */
export function text(document: DocumentLike, value: string): NodeLike {
  return document.createTextNode(value);
}

/**
 * A label for untrusted text: an isolated span whose direction comes from its
 * own content, with hidden characters shown as markers and, when there are any,
 * a warning badge that names them for assistive technology.
 */
export function label(document: DocumentLike, value: string, maxCodePoints: number = DEFAULT_LABEL_LENGTH): ElementLike {
  const safe = safeLabel(value, maxCodePoints);
  const children: Child[] = [safe.text];
  if (safe.warnings.length > 0) {
    const count = (kind: string): number => safe.warnings.filter((warning) => warning.kind === kind).length;
    const parts: string[] = [];
    for (const [kind, noun] of [["bidi-control", "hidden text-direction character"], ["control", "control character"], ["invisible", "invisible character"]] as const) {
      const n = count(kind);
      if (n > 0) {
        parts.push(`${n} ${noun}${n === 1 ? "" : "s"}`);
      }
    }
    children.push(
      element(document, "span", { class: "ws-hidden-characters", role: "img", "aria-label": `Contains ${parts.join(" and ")}` }, ["⚠"]),
    );
  }
  const classes = safe.warnings.length > 0 ? "ws-label ws-label-flagged" : "ws-label";
  return element(document, "span", { class: classes, dir: "auto" }, children);
}
