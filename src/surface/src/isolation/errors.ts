/**
 * Raised when the surface is asked to do something its isolation rules forbid.
 * The message names the rule, never the offending content, so hostile input is
 * not echoed into logs or the user interface.
 */
export class IsolationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "IsolationError";
  }
}
