/**
 * The shape of `navigator.modelContext`, declared here because no library ships
 * it yet.
 *
 * WebMCP is under incubation at the W3C Web Machine Learning Community Group
 * (Google and Microsoft), and Chrome shipped `navigator.modelContext` in
 * February 2026. **It is not on the standards track and it is Chrome-only**, and
 * that label travels with it everywhere in this repository rather than being
 * discovered later by a reader.
 *
 * Declaring the surface ourselves rather than depending on a typings package is
 * the same call ADR 0006 makes about dependencies: this is thirty lines, the
 * specification will move, and a package that lags the browser is worse than a
 * file we can edit.
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
  execute(input: Readonly<Record<string, unknown>>): Promise<ToolResponse>;
}

export interface ModelContext {
  provideContext(context: { readonly tools: readonly ToolDescriptor[] }): void;
}

/**
 * Whether this browser can host an agent at all.
 *
 * The feature detection IS the degradation (CLAUDE.md, invariant 8): without it
 * nothing registers, nothing is imported, and the shop is exactly the shop it
 * was. There is no flag and no fallback path to maintain, because the absence of
 * a fallback is the point — WebMCP adds a surface, it does not replace one.
 */
export function supportsModelContext(target: Navigator = navigator): boolean {
  return 'modelContext' in target && typeof target.modelContext === 'object';
}

declare global {
  interface Navigator {
    readonly modelContext?: ModelContext;
  }
}
