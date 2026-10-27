import { test } from "node:test";
import assert from "node:assert/strict";
import { safeLabel, hasHiddenCharacters } from "../src/isolation/index.ts";

const BIDI = [0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066, 0x2067, 0x2068, 0x2069, 0x200e, 0x200f, 0x061c];

for (const codePoint of BIDI) {
  const hex = codePoint.toString(16).toUpperCase().padStart(4, "0");
  test(`bidi control U+${hex} becomes a visible marker with a warning`, () => {
    const value = `access${String.fromCodePoint(codePoint)}Level`;
    const label = safeLabel(value);
    assert.equal(label.text, `access⟦U+${hex}⟧Level`);
    assert.deepEqual(label.warnings, [{ kind: "bidi-control", codePoint: `U+${hex}`, index: 6 }]);
    assert.equal(hasHiddenCharacters(value), true);
  });
}

test("the Trojan Source pattern is shown, not applied", () => {
  const value = 'isAdmin = false; /*‮ } ⁦if (isAdmin)⁩ ⁦ begin admins only */';
  const label = safeLabel(value);
  assert.equal(label.warnings.length, 4);
  assert.ok(!/[‪-‮⁦-⁩]/u.test(label.text));
});

for (const [name, value, hex] of [["null", "\u0000", "0000"], ["bell", "\u0007", "0007"], ["escape", "\u001b", "001B"], ["newline", "\n", "000A"], ["delete", "\u007f", "007F"], ["C1 control", "\u0085", "0085"], ["lone surrogate", "\ud800", "D800"]] as const) {
  test(`${name} becomes a visible control marker`, () => {
    const label = safeLabel(`a${value}b`);
    assert.equal(label.text, `a⟦U+${hex}⟧b`);
    assert.equal(label.warnings[0]?.kind, "control");
  });
}

test("Arabic and Hebrew text passes through unchanged", () => {
  for (const value of ["مرحبا", "שלום", "total مجموع sum"]) {
    const label = safeLabel(value);
    assert.equal(label.text, value);
    assert.equal(label.warnings.length, 0);
    assert.equal(hasHiddenCharacters(value), false);
  }
});

test("joiners that scripts need are not flagged", () => {
  assert.equal(safeLabel("م‌م‍").warnings.length, 0);
});

test("long text is cut by code points without splitting a surrogate pair", () => {
  const label = safeLabel("\u{1F600}".repeat(10), 3);
  assert.equal(label.text, "\u{1F600}\u{1F600}\u{1F600}…");
  assert.equal(label.truncated, true);
});

// Characters that render as nothing (the Reviewer's review 01M5WSE5K4WPYPY5V9PAHDKWK9).
const INVISIBLE = [0x200b, 0x2060, 0x2061, 0x2062, 0x2063, 0x2064, 0x206a, 0x206f, 0xfeff, 0x00ad, 0x034f, 0x180e, 0x3164,
  0xfff9, 0xfffa, 0xfffb, 0x2028, 0x2029, 0xe0001, 0xe0020, 0xe007f, 0x1d173, 0x1d17a];

for (const codePoint of INVISIBLE) {
  const hex = codePoint.toString(16).toUpperCase().padStart(4, "0");
  test(`invisible U+${hex} becomes a visible marker with a warning`, () => {
    const result = safeLabel(`total${String.fromCodePoint(codePoint)}Count`);
    assert.equal(result.text, `total⟦U+${hex}⟧Count`);
    assert.deepEqual(result.warnings, [{ kind: "invisible", codePoint: `U+${hex}`, index: 5 }]);
    assert.equal(hasHiddenCharacters(String.fromCodePoint(codePoint)), true);
  });
}

test("two names that differ by a zero-width space no longer look alike", () => {
  assert.notEqual(safeLabel("total").text, safeLabel("to​tal").text);
});

test("joiners and variation selectors that shape scripts and emoji stay unmarked", () => {
  for (const value of ["👩‍💻", "می‌خواهم", "❤️", "邊\u{e0100}"]) {
    assert.deepEqual(safeLabel(value), { text: value, warnings: [], truncated: false }, value);
    assert.equal(hasHiddenCharacters(value), false, value);
  }
});

test("Mongolian free variation selectors shape text and stay unmarked", () => {
  for (const value of ["ᠠ᠋", "ᠠ᠌", "ᠠ᠍", "ᠠ᠏"]) {
    assert.deepEqual(safeLabel(value).warnings, [], value);
  }
  assert.equal(safeLabel("ᠠ᠎").warnings[0]?.kind, "invisible");
});
