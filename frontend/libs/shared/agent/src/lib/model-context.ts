/**
 * The shape of `document.modelContext` the shop uses, declared here because no
 * library shipped it when this was written.
 *
 * Angular 22.2.5 now carries the draft's types inside `@angular/core`
 * (`third_party/webmcp-types`, MIT) and declares `Document.modelContext` globally
 * as `WebMCP.ModelContext`, so this file no longer augments `Document`: a second
 * declaration of the same member is TS2717. The interfaces below stay as the
 * narrower shape the shop registers through, and the browser's
 * `WebMCP.ModelContext` is assignable to it.
 *
 * WebMCP is under incubation at the W3C Web Machine Learning Community Group
 * (Google and Microsoft). **It is not on the standards track and it is
 * Chrome-only**, and that label travels with it everywhere in this repository
 * rather than being discovered later by a reader.
 *
 * Declaring the surface ourselves rather than depending on a typings package is
 * the same call ADR 0006 makes about dependencies: this is forty lines, the
 * specification will move, and a package that lags the browser is worse than a
 * file we can edit.
 *
 * **And it did move.** This file was first written against what Chrome shipped
 * in February 2026 — `navigator.modelContext.provideContext({ tools })`, the
 * whole set in one call. The Community Group's draft of 9 October 2026 has
 * neither: the context hangs from the `Document`, a tool is registered by a
 * call of its own that answers with a promise, it is taken back by aborting a
 * signal, and it says of itself whether it only reads. What follows is that
 * draft's IDL, member for member, for the part the shop uses.
 */

/** A parameter schema, in the JSON Schema subset the specification uses. */
export interface ToolInputSchema {
  readonly type: 'object';
  readonly properties: Readonly<Record<string, unknown>>;
  readonly required?: readonly string[];
}

/** What a tool hands back. Text content, in the MCP shape. */
export interface ToolResponse {
  readonly content: readonly { readonly type: 'text'; readonly text: string }[];
  readonly isError?: boolean;
}

/**
 * What a tool says of itself (the draft's `ToolAnnotations`). Each is a hint an
 * agent may act on and false when left unsaid — so a tool that only reads has
 * to say so, or an agent that asks before anything that might change something
 * asks before every search.
 */
export interface ToolAnnotations {
  /** It changes no state: it only reads. */
  readonly readOnlyHint?: boolean;
  /** Its answer carries text the page's author does not vouch for. */
  readonly untrustedContentHint?: boolean;
  /** What it does is significant, real-world or cannot be taken back. */
  readonly consequentialHint?: boolean;
}

/** What the browser hands a tool when it runs it (`ToolExecuteCallbackOptions`). */
export interface ToolExecuteOptions {
  /** Aborted when the agent gives the call up. */
  readonly signal: AbortSignal;
}

/** The draft's `ModelContextTool`. */
export interface ToolDescriptor {
  readonly name: string;

  /**
   * What the tool does, written for a MODEL rather than for a developer.
   *
   * It is the only thing that decides whether the tool gets called correctly, so
   * it says what the tool is for and what it refuses — "adds a specific SKU to
   * the shopper's cart; it does not choose the variant" is worth more than
   * "adds to cart".
   */
  readonly description: string;
  readonly inputSchema: ToolInputSchema;
  readonly annotations?: ToolAnnotations;

  /**
   * The draft types the answer `Promise<any>`; the shop's is MCP's content
   * shape, because that is what an agent already knows how to read — and
   * `isError` is how a refusal arrives as one.
   */
  execute(input: Readonly<Record<string, unknown>>, options: ToolExecuteOptions): Promise<ToolResponse>;
}

/** How a tool is registered (`ModelContextRegisterToolOptions`). */
export interface RegisterToolOptions {
  /** Aborting it takes the tool back: the draft has no `unregisterTool`. */
  readonly signal?: AbortSignal;
}

export interface ModelContext {
  /**
   * One tool. The promise is rejected with `InvalidStateError` for a name
   * already registered, an empty description, or a name that is not letters,
   * digits, `_`, `-` and `.`; with `NotAllowedError` where the page is not
   * allowed the feature.
   */
  registerTool(tool: ToolDescriptor, options?: RegisterToolOptions): Promise<void>;
}

/**
 * Whether this browser can host an agent at all.
 *
 * The feature detection IS the degradation (CLAUDE.md, invariant 8): without it
 * nothing registers, nothing is imported, and the shop is exactly the shop it
 * was. There is no flag and no fallback path to maintain, because the absence of
 * a fallback is the point — WebMCP adds a surface, it does not replace one.
 *
 * Detected by the method the shop calls, not by the name it hangs from: a
 * browser still on the first shape has a `modelContext` with no `registerTool`,
 * and there too the right thing to do is nothing.
 */
export function supportsModelContext(target: Document = document): boolean {
  return typeof target.modelContext?.registerTool === 'function';
}
