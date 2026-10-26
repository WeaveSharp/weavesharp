import { contentSecurityPolicy, resourceOriginAllows } from "./csp.ts";
import { IsolationError } from "./errors.ts";

/**
 * The one page every host serves the surface in: a fixed HTML shell whose
 * Content-Security-Policy comes first, with the page load's nonce on each script
 * and style. Hosts pass values, never markup. Every value is checked, then
 * escaped for the attribute or text it lands in, so a host's paths or titles
 * cannot change the page's structure.
 */

export interface PageOptions {
  /** The page load's nonce, from createNonce. */
  readonly nonce: string;
  /** HTTPS origins the host serves the surface's files from, as contentSecurityPolicy takes them. */
  readonly resourceOrigins: readonly string[];
  /** Module scripts, in order: HTTPS URLs under a declared resource origin. */
  readonly scripts: readonly string[];
  /** Stylesheets: HTTPS URLs under a declared resource origin. */
  readonly styles?: readonly string[];
  /** The page title, shown by assistive technology. */
  readonly title: string;
  /** The page's language tag; English by default. */
  readonly lang?: string;
}

/** The id of the element the surface renders into. */
export const PAGE_ROOT = "weavesharp-root";

const LANGUAGE_TAG = /^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,4}$/;
const MAX_TITLE = 256;

function escape(value: string): string {
  return value.replace(/[&<>"']/g, (character) => `&#${character.charCodeAt(0)};`);
}

function resource(url: string, origins: readonly string[]): string {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    throw new IsolationError("A page resource must be an absolute URL.");
  }
  if (parsed.protocol !== "https:" || parsed.username !== "" || parsed.password !== "" || parsed.hash !== "") {
    throw new IsolationError("A page resource must be an HTTPS URL without credentials or a fragment.");
  }
  if (!origins.some((origin) => resourceOriginAllows(origin, parsed.origin))) {
    throw new IsolationError("A page resource must come from a declared resource origin.");
  }
  return escape(parsed.href);
}

/** Builds the surface's page; throws if any value would break its rules. */
export function surfacePage(options: PageOptions): string {
  const policy = contentSecurityPolicy({ nonce: options.nonce, resourceOrigins: options.resourceOrigins });
  if (options.scripts.length === 0) {
    throw new IsolationError("A page needs at least one script.");
  }
  const title = options.title;
  if (title.length === 0 || title.length > MAX_TITLE || /[\u0000-\u001f\u007f-\u009f]/.test(title)) {
    throw new IsolationError("A page title must be 1 to 256 characters without control characters.");
  }
  const lang = options.lang ?? "en";
  if (!LANGUAGE_TAG.test(lang)) {
    throw new IsolationError("A page language must be a language tag.");
  }
  const nonce = escape(options.nonce);
  const styles = (options.styles ?? []).map((url) => `<link rel="stylesheet" href="${resource(url, options.resourceOrigins)}" nonce="${nonce}">`);
  const scripts = options.scripts.map((url) => `<script type="module" src="${resource(url, options.resourceOrigins)}" nonce="${nonce}"></script>`);
  return [
    "<!DOCTYPE html>",
    `<html lang="${escape(lang)}">`,
    "<head>",
    // The encoding comes first, within the first 1,024 bytes; the policy precedes every resource.
    `<meta charset="utf-8">`,
    `<meta http-equiv="Content-Security-Policy" content="${escape(policy)}">`,
    `<meta name="viewport" content="width=device-width, initial-scale=1">`,
    `<title>${escape(title)}</title>`,
    ...styles,
    "</head>",
    "<body>",
    `<div id="${PAGE_ROOT}"></div>`,
    ...scripts,
    "</body>",
    "</html>",
    "",
  ].join("\n");
}
