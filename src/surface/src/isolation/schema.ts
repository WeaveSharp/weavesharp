import { IsolationError } from "./errors.ts";

/**
 * A validator for exactly the JSON Schema (draft 2020-12) keywords protocol v0's
 * schemas use. A schema with any other keyword is refused rather than half
 * understood. Size bounds are checked before content, and the work done for one
 * value is bounded, so a hostile message cannot make validation expensive.
 */

/** Schema files by name, as protocol/v0 holds them, for example "envelope.schema.json". */
export type SchemaSet = ReadonlyMap<string, unknown>;

export interface SchemaIssue {
  /** A JSON pointer to the failing value. */
  readonly path: string;
  /** The keyword that failed. */
  readonly keyword: string;
}

const ANNOTATIONS = new Set(["$schema", "$id", "$defs", "$comment", "title", "description"]);
const KEYWORDS = new Set([
  "$ref", "type", "const", "enum", "oneOf",
  "properties", "patternProperties", "additionalProperties", "propertyNames", "required", "maxProperties",
  "items", "minItems", "maxItems", "uniqueItems",
  "minLength", "maxLength", "pattern", "minimum", "maximum",
]);

const STEP_BUDGET = 2_000_000;

type JsonObject = Readonly<Record<string, unknown>>;

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function equal(a: unknown, b: unknown): boolean {
  if (a === b) {
    return true;
  }
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((item, i) => equal(item, b[i]));
  }
  if (isObject(a) && isObject(b)) {
    const keys = Object.keys(a);
    return keys.length === Object.keys(b).length && keys.every((key) => Object.hasOwn(b, key) && equal(a[key], b[key]));
  }
  return false;
}

function pointerEscape(segment: string): string {
  return segment.replaceAll("~", "~0").replaceAll("/", "~1");
}

/** Every keyword the schemas use, so a test can show the validator implements all of them. */
export function unsupportedKeywords(schemas: SchemaSet): string[] {
  const found = new Set<string>();
  const walk = (node: unknown): void => {
    if (Array.isArray(node)) {
      node.forEach(walk);
    } else if (isObject(node)) {
      for (const [key, value] of Object.entries(node)) {
        if (!ANNOTATIONS.has(key) && !KEYWORDS.has(key)) {
          found.add(key);
        }
        if (key === "properties" || key === "patternProperties" || key === "$defs") {
          if (isObject(value)) {
            Object.values(value).forEach(walk);
          }
        } else if (key !== "const" && key !== "enum") {
          walk(value);
        }
      }
    }
  };
  for (const schema of schemas.values()) {
    walk(schema);
  }
  return [...found].sort();
}

class Validator {
  private steps = 0;
  private readonly patterns = new Map<string, RegExp>();
  readonly issues: SchemaIssue[] = [];
  private readonly schemas: SchemaSet;

  constructor(schemas: SchemaSet) {
    this.schemas = schemas;
  }

  private resolve(file: string, reference: string): { file: string; schema: unknown } {
    const [target = "", fragment = ""] = reference.split("#", 2);
    const targetFile = target.length > 0 ? target : file;
    if (!this.schemas.has(targetFile)) {
      throw new IsolationError("A schema reference names a file outside the schema set.");
    }
    let node: unknown = this.schemas.get(targetFile);
    for (const raw of fragment.split("/").filter((part) => part.length > 0)) {
      const segment = raw.replaceAll("~1", "/").replaceAll("~0", "~");
      if (!isObject(node) || !Object.hasOwn(node, segment)) {
        throw new IsolationError("A schema reference does not resolve.");
      }
      node = node[segment];
    }
    return { file: targetFile, schema: node };
  }

  private pattern(source: string): RegExp {
    let compiled = this.patterns.get(source);
    if (compiled === undefined) {
      compiled = new RegExp(source, "u");
      this.patterns.set(source, compiled);
    }
    return compiled;
  }

  /** Validates value against schema, recording issues; returns whether it is valid. */
  check(file: string, schema: unknown, value: unknown, path: string, record = true): boolean {
    if (++this.steps > STEP_BUDGET) {
      throw new IsolationError("Validation exceeded its work budget.");
    }
    if (!isObject(schema)) {
      throw new IsolationError("A schema node is not an object.");
    }
    const fail = (keyword: string): false => {
      if (record) {
        this.issues.push({ path, keyword });
      }
      return false;
    };
    for (const key of Object.keys(schema)) {
      if (!ANNOTATIONS.has(key) && !KEYWORDS.has(key)) {
        throw new IsolationError("A schema uses a keyword this validator does not implement.");
      }
    }
    if (typeof schema["$ref"] === "string") {
      const target = this.resolve(file, schema["$ref"]);
      if (!this.check(target.file, target.schema, value, path, record)) {
        return false;
      }
    }
    if (schema["type"] !== undefined && !this.hasType(schema["type"], value)) {
      return fail("type");
    }
    if (Object.hasOwn(schema, "const") && !equal(schema["const"], value)) {
      return fail("const");
    }
    if (Array.isArray(schema["enum"]) && !schema["enum"].some((option) => equal(option, value))) {
      return fail("enum");
    }
    let valid = true;
    if (typeof value === "string") {
      valid = this.checkString(schema, value, fail) && valid;
    } else if (typeof value === "number") {
      if (typeof schema["minimum"] === "number" && value < schema["minimum"]) {
        valid = fail("minimum");
      }
      if (typeof schema["maximum"] === "number" && value > schema["maximum"]) {
        valid = fail("maximum");
      }
    } else if (Array.isArray(value)) {
      valid = this.checkArray(file, schema, value, path, record, fail) && valid;
    } else if (isObject(value)) {
      valid = this.checkObject(file, schema, value, path, record, fail) && valid;
    }
    if (Array.isArray(schema["oneOf"])) {
      const matches = schema["oneOf"].filter((option) => this.check(file, option, value, path, false)).length;
      if (matches !== 1) {
        valid = fail("oneOf");
      }
    }
    return valid;
  }

  private hasType(type: unknown, value: unknown): boolean {
    const types = Array.isArray(type) ? type : [type];
    return types.some((name) => {
      switch (name) {
        case "string": return typeof value === "string";
        case "boolean": return typeof value === "boolean";
        case "null": return value === null;
        case "integer": return Number.isSafeInteger(value);
        case "number": return typeof value === "number" && Number.isFinite(value);
        case "array": return Array.isArray(value);
        case "object": return isObject(value);
        default: throw new IsolationError("A schema names an unknown type.");
      }
    });
  }

  private checkString(schema: JsonObject, value: string, fail: (keyword: string) => false): boolean {
    // Bounds first, on the raw length, before counting code points or running a pattern.
    if (typeof schema["maxLength"] === "number" && value.length > schema["maxLength"] * 2) {
      return fail("maxLength");
    }
    const length = [...value].length;
    if (typeof schema["maxLength"] === "number" && length > schema["maxLength"]) {
      return fail("maxLength");
    }
    if (typeof schema["minLength"] === "number" && length < schema["minLength"]) {
      return fail("minLength");
    }
    if (typeof schema["pattern"] === "string" && !this.pattern(schema["pattern"]).test(value)) {
      return fail("pattern");
    }
    return true;
  }

  private checkArray(file: string, schema: JsonObject, value: readonly unknown[], path: string, record: boolean, fail: (keyword: string) => false): boolean {
    if (typeof schema["maxItems"] === "number" && value.length > schema["maxItems"]) {
      return fail("maxItems");
    }
    if (typeof schema["minItems"] === "number" && value.length < schema["minItems"]) {
      return fail("minItems");
    }
    let valid = true;
    if (schema["items"] !== undefined) {
      value.forEach((item, i) => {
        valid = this.check(file, schema["items"], item, `${path}/${i}`, record) && valid;
      });
    }
    if (schema["uniqueItems"] === true) {
      for (let i = 0; i < value.length && valid; i++) {
        for (let j = i + 1; j < value.length; j++) {
          if (++this.steps > STEP_BUDGET) {
            throw new IsolationError("Validation exceeded its work budget.");
          }
          if (equal(value[i], value[j])) {
            return fail("uniqueItems");
          }
        }
      }
    }
    return valid;
  }

  private checkObject(file: string, schema: JsonObject, value: JsonObject, path: string, record: boolean, fail: (keyword: string) => false): boolean {
    const keys = Object.keys(value);
    if (typeof schema["maxProperties"] === "number" && keys.length > schema["maxProperties"]) {
      return fail("maxProperties");
    }
    let valid = true;
    for (const required of Array.isArray(schema["required"]) ? schema["required"] : []) {
      if (typeof required === "string" && !Object.hasOwn(value, required)) {
        valid = fail("required");
      }
    }
    const properties = isObject(schema["properties"]) ? schema["properties"] : {};
    const patterns = isObject(schema["patternProperties"]) ? Object.entries(schema["patternProperties"]) : [];
    for (const key of keys) {
      const child = `${path}/${pointerEscape(key)}`;
      if (schema["propertyNames"] !== undefined && !this.check(file, schema["propertyNames"], key, child, record)) {
        valid = false;
      }
      let matched = false;
      if (Object.hasOwn(properties, key)) {
        matched = true;
        valid = this.check(file, properties[key], value[key], child, record) && valid;
      }
      for (const [source, subschema] of patterns) {
        if (this.pattern(source).test(key)) {
          matched = true;
          valid = this.check(file, subschema, value[key], child, record) && valid;
        }
      }
      if (!matched && schema["additionalProperties"] !== undefined) {
        if (schema["additionalProperties"] === false) {
          valid = fail("additionalProperties");
        } else if (schema["additionalProperties"] !== true) {
          valid = this.check(file, schema["additionalProperties"], value[key], child, record) && valid;
        }
      }
    }
    return valid;
  }
}

/** Validates a value against one schema file of the set; an empty list means it is valid. */
export function validate(schemas: SchemaSet, file: string, value: unknown): readonly SchemaIssue[] {
  if (!schemas.has(file)) {
    throw new IsolationError("The schema set has no such file.");
  }
  const validator = new Validator(schemas);
  validator.check(file, schemas.get(file), value, "");
  return validator.issues;
}
