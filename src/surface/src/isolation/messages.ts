import { IsolationError } from "./errors.ts";
import { validate, type SchemaSet } from "./schema.ts";

/**
 * The surface's message boundary. Everything that arrives from the host is JSON
 * text checked against bounds before it is parsed, refused if it carries keys
 * that could reach an object's prototype, limited to the protocol families and
 * kinds the surface may receive, and validated against protocol v0's schema for
 * its family. Everything the surface sends is one of the commands it may send,
 * validated the same way. A refused message is reported by rule, never by
 * content.
 */

/** Families and kinds the surface accepts from its host. */
export const INBOUND: Readonly<Record<string, readonly string[]>> = {
  "initialize": ["response"],
  "publish-graph": ["response", "notification"],
  "publish-execution-state": ["notification"],
  "edit-result": ["response"],
  "read-or-save-layout": ["response"],
  "cancel-or-dispose": ["notification"],
};

/** Families and kinds the surface may send: its command allow-list. */
export const OUTBOUND: Readonly<Record<string, readonly string[]>> = {
  "request-graph": ["query"],
  "prepare-edit": ["query"],
  "edit-result": ["mutation"],
  "read-or-save-layout": ["query", "mutation"],
  "cancel-or-dispose": ["notification"],
};

export interface MessageLimits {
  /** The longest message accepted, in UTF-16 code units, checked before parsing. */
  readonly maxCharacters: number;
  /** The deepest nesting of objects and arrays accepted, checked before parsing. */
  readonly maxDepth: number;
}

export const DEFAULT_LIMITS: MessageLimits = { maxCharacters: 8 * 1024 * 1024, maxDepth: 32 };

export type InboundResult =
  | { readonly accepted: true; readonly family: string; readonly message: Readonly<Record<string, unknown>> }
  | { readonly accepted: false; readonly reason: string };

const FORBIDDEN_KEYS = new Set(["__proto__", "constructor", "prototype"]);

/** The deepest nesting in JSON text, found without parsing it; strings are skipped. */
export function nestingDepth(text: string): number {
  let depth = 0;
  let deepest = 0;
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (inString) {
      if (c === 0x5c) {
        i++; // skip the escaped character
      } else if (c === 0x22) {
        inString = false;
      }
    } else if (c === 0x22) {
      inString = true;
    } else if (c === 0x7b || c === 0x5b) {
      depth++;
      if (depth > deepest) {
        deepest = depth;
      }
    } else if (c === 0x7d || c === 0x5d) {
      depth--;
    }
  }
  return deepest;
}

function forbiddenKey(value: unknown): boolean {
  if (Array.isArray(value)) {
    return value.some(forbiddenKey);
  }
  if (typeof value === "object" && value !== null) {
    for (const [key, child] of Object.entries(value)) {
      if (FORBIDDEN_KEYS.has(key) || forbiddenKey(child)) {
        return true;
      }
    }
  }
  return false;
}

function deepFreeze<T>(value: T): T {
  if (typeof value === "object" && value !== null) {
    for (const child of Object.values(value)) {
      deepFreeze(child);
    }
    Object.freeze(value);
  }
  return value;
}

function familyAndKind(value: unknown): { family: string; kind: string } | undefined {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return undefined;
  }
  const record = value as Record<string, unknown>;
  const family = record["family"];
  const kind = record["kind"];
  return typeof family === "string" && typeof kind === "string" ? { family, kind } : undefined;
}

function allowed(table: Readonly<Record<string, readonly string[]>>, family: string, kind: string): boolean {
  return Object.hasOwn(table, family) && (table[family] ?? []).includes(kind);
}

/** Reads one message from the host, or says which rule refused it. */
export function readInbound(raw: unknown, schemas: SchemaSet, limits: MessageLimits = DEFAULT_LIMITS): InboundResult {
  if (typeof raw !== "string") {
    return { accepted: false, reason: "Messages arrive as JSON text." };
  }
  if (raw.length > limits.maxCharacters) {
    return { accepted: false, reason: "The message is longer than the limit." };
  }
  if (nestingDepth(raw) > limits.maxDepth) {
    return { accepted: false, reason: "The message nests deeper than the limit." };
  }
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return { accepted: false, reason: "The message is not valid JSON." };
  }
  if (forbiddenKey(value)) {
    return { accepted: false, reason: "The message carries a forbidden key." };
  }
  const header = familyAndKind(value);
  if (header === undefined) {
    return { accepted: false, reason: "The message has no family and kind." };
  }
  if (!allowed(INBOUND, header.family, header.kind)) {
    return { accepted: false, reason: "The surface does not accept that family and kind." };
  }
  const issues = validate(schemas, `${header.family}.schema.json`, value);
  if (issues.length > 0) {
    return { accepted: false, reason: `The message breaks its schema at ${issues[0]?.path || "/"} (${issues[0]?.keyword}).` };
  }
  return { accepted: true, family: header.family, message: deepFreeze(value as Record<string, unknown>) };
}

/** Serializes a command for the host; throws unless it is an allowed, schema-valid command. */
export function writeOutbound(message: unknown, schemas: SchemaSet, limits: MessageLimits = DEFAULT_LIMITS): string {
  const header = familyAndKind(message);
  if (header === undefined || !allowed(OUTBOUND, header.family, header.kind)) {
    throw new IsolationError("The surface does not send that family and kind.");
  }
  if (forbiddenKey(message)) {
    throw new IsolationError("The command carries a forbidden key.");
  }
  const issues = validate(schemas, `${header.family}.schema.json`, message);
  if (issues.length > 0) {
    throw new IsolationError(`The command breaks its schema at ${issues[0]?.path || "/"} (${issues[0]?.keyword}).`);
  }
  const text = JSON.stringify(message);
  if (text.length > limits.maxCharacters) {
    throw new IsolationError("The command is longer than the limit.");
  }
  return text;
}
