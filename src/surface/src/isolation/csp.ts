import { IsolationError } from "./errors.ts";

/** A nonce: at least 128 bits of randomness, in base64. */
const NONCE_PATTERN = /^[A-Za-z0-9+/]{22,}={0,2}$/;

/**
 * An origin a host serves the surface's own scripts, styles and fonts from: HTTPS
 * only, an optional leading "*." for a host's own resource domain (VS Code's
 * webview resource origin takes that form), never a bare wildcard, a scheme alone,
 * a path or a keyword.
 */
const RESOURCE_ORIGIN_PATTERN = /^https:\/\/(\*\.)?[a-z0-9-]+(\.[a-z0-9-]+)+(:[0-9]{1,5})?$/;

/** A source of cryptographically random bytes; the platform's by default. */
export type RandomSource = (bytes: Uint8Array<ArrayBuffer>) => Uint8Array<ArrayBuffer>;

/** Creates a fresh nonce for one page load, from the platform's cryptographic random source. */
export function createNonce(random: RandomSource = (bytes) => crypto.getRandomValues(bytes)): string {
  const bytes = random(new Uint8Array(16));
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary);
}

export interface PolicyOptions {
  /** The page load's nonce; every script and style element must carry it. */
  readonly nonce: string;
  /** Origins the host serves the surface's own files from; none means everything is inline with the nonce. */
  readonly resourceOrigins?: readonly string[];
}

/**
 * The graph surface's Content-Security-Policy. Nothing loads or runs unless it is
 * the surface's own nonce-carrying script or style, or a file from a declared
 * resource origin. No network connections, frames, workers, plugins, forms or base
 * URL changes are allowed, and Trusted Types are required with no policy, so
 * every HTML-string and script-string sink throws in the Chromium-based webviews
 * all three hosts use. frame-ancestors cannot be set from a meta element; hosts
 * set it in a header where they can.
 */
export function contentSecurityPolicy(options: PolicyOptions): string {
  if (!NONCE_PATTERN.test(options.nonce)) {
    throw new IsolationError("The nonce must be at least 128 bits of base64.");
  }
  const origins = options.resourceOrigins ?? [];
  for (const origin of origins) {
    if (!RESOURCE_ORIGIN_PATTERN.test(origin)) {
      throw new IsolationError("A resource origin must be an HTTPS origin without a path, keyword or bare wildcard.");
    }
  }
  const own = origins.join(" ");
  const withOwn = (first: string): string => (own.length > 0 ? `${first} ${own}` : first);
  const directives = [
    "default-src 'none'",
    `script-src ${withOwn(`'nonce-${options.nonce}'`)}`,
    `style-src ${withOwn(`'nonce-${options.nonce}'`)}`,
    own.length > 0 ? `img-src ${own}` : "img-src 'none'",
    own.length > 0 ? `font-src ${own}` : "font-src 'none'",
    "connect-src 'none'",
    "media-src 'none'",
    "object-src 'none'",
    "frame-src 'none'",
    "child-src 'none'",
    "worker-src 'none'",
    "manifest-src 'none'",
    "base-uri 'none'",
    "form-action 'none'",
    "require-trusted-types-for 'script'",
    "trusted-types 'none'",
  ];
  return directives.join("; ");
}
