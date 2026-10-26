/**
 * Text from source code, names, comments and metadata is data. Before it reaches
 * a label it is bounded, its bidirectional, control and invisible characters are
 * made visible, and the label isolates its own direction (design/layout-and-
 * direction.md, section 4; the Trojan Source attack, CVE-2021-42574). C# accepts
 * format characters in identifiers, so two names that differ only by an invisible
 * character must not look alike.
 */

/** Embeddings, overrides, isolates and marks that reorder text without being seen. */
const BIDI_CONTROLS: ReadonlySet<number> = new Set([
  0x202a, 0x202b, 0x202c, 0x202d, 0x202e, // embeddings, pop and overrides
  0x2066, 0x2067, 0x2068, 0x2069, // isolates
  0x200e, 0x200f, 0x061c, // left-to-right, right-to-left and Arabic letter marks
]);

export type LabelWarningKind = "bidi-control" | "control" | "invisible";

/**
 * Characters that render as nothing: default-ignorable code points, format
 * characters and the line and paragraph separators. Zero-width joiners and
 * non-joiners and variation selectors stay unmarked, because scripts and emoji
 * need them to shape correctly.
 */
const INVISIBLE = /^[\p{Default_Ignorable_Code_Point}\p{Cf}\p{Zl}\p{Zp}]$/u;

function shapesText(value: number): boolean {
  return value === 0x200c || value === 0x200d || (value >= 0xfe00 && value <= 0xfe0f) || (value >= 0xe0100 && value <= 0xe01ef);
}

export interface LabelWarning {
  readonly kind: LabelWarningKind;
  /** The code point, as U+XXXX. */
  readonly codePoint: string;
  /** Its position, counted in code points of the original text. */
  readonly index: number;
}

export interface SafeLabel {
  /** The text to show, with every hidden character replaced by a visible marker. */
  readonly text: string;
  readonly warnings: readonly LabelWarning[];
  readonly truncated: boolean;
}

/** The longest label shown, in code points, before it is cut with an ellipsis. */
export const DEFAULT_LABEL_LENGTH = 512;

function formatCodePoint(value: number): string {
  return `U+${value.toString(16).toUpperCase().padStart(4, "0")}`;
}

function kindOf(value: number): LabelWarningKind | undefined {
  if (BIDI_CONTROLS.has(value)) {
    return "bidi-control";
  }
  // C0 and C1 controls, delete, and lone surrogates (possible in JavaScript strings).
  if (value < 0x20 || (value >= 0x7f && value <= 0x9f) || (value >= 0xd800 && value <= 0xdfff)) {
    return "control";
  }
  if (!shapesText(value) && INVISIBLE.test(String.fromCodePoint(value))) {
    return "invisible";
  }
  return undefined;
}

/** Makes any text safe to show as a label: bounded, with hidden characters made visible. */
export function safeLabel(value: string, maxCodePoints: number = DEFAULT_LABEL_LENGTH): SafeLabel {
  const warnings: LabelWarning[] = [];
  let text = "";
  let index = 0;
  let truncated = false;
  for (const character of value) {
    if (index >= maxCodePoints) {
      truncated = true;
      break;
    }
    const codePoint = character.codePointAt(0) ?? 0;
    const kind = kindOf(codePoint);
    if (kind === undefined) {
      text += character;
    } else {
      const formatted = formatCodePoint(codePoint);
      warnings.push({ kind, codePoint: formatted, index });
      text += `⟦${formatted}⟧`;
    }
    index += 1;
  }
  if (truncated) {
    text += "…";
  }
  return { text, warnings, truncated };
}

/** True when the text holds any character a label would replace with a marker. */
export function hasHiddenCharacters(value: string): boolean {
  for (const character of value) {
    if (kindOf(character.codePointAt(0) ?? 0) !== undefined) {
      return true;
    }
  }
  return false;
}
