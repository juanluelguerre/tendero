import { inject, Injectable, InjectionToken } from '@angular/core';
import {
  supportsModelContext,
  type ModelContext,
  type ToolDescriptor,
  type ToolResponse,
} from './model-context';

/**
 * Registers this page's tools with the browser's agent, when there is one.
 *
 * **This is the MECHANISM and it is shared; the TOOLS are identity and stay per
 * app** (ADR 0010). The storefront offers searching and adding to a cart; the
 * backoffice will offer something else entirely when the copilot arrives, and a
 * single tool set that had to be both would be a variant matrix.
 *
 * ## The thing worth understanding about this surface
 *
 * WebMCP is the cheapest of Tendero's three agent surfaces to build and the most
 * interesting to explain, and the reason is not the transport. It is the TRUST
 * MODEL:
 *
 * | surface | the agent is | authorization |
 * |---|---|---|
 * | **WebMCP** | code inside the user's own browser session | **inherits** it — no second principal, no token, no mandate |
 * | MCP server | an external process | `AllowAnonymous` by explicit decision; read-only |
 * | UCP + AP2 | its own principal, server to server | bearer token plus a signed mandate |
 *
 * An agent here is already the shopper. It has their cart token, their language,
 * their session — because it is running in their tab. That is why this phase
 * needs no work in `Accounts` and phase 11 needs a whole context: they are not
 * two implementations of one idea, they are answers to different questions.
 *
 * ## What keeps it honest
 *
 * Tools call **the same services the interface calls**. Never a parallel HTTP
 * path, never a second copy of a rule. It is the frontend sibling of "MCP is a
 * transport over the query dispatcher": if adding to the cart through the agent
 * could take a different route from pressing the button, the two would drift and
 * only one of them would have tests.
 */
@Injectable({ providedIn: 'root' })
export class AgentTools {
  private readonly context = inject(MODEL_CONTEXT, { optional: true });

  private registered = false;

  /**
   * Whether the browser can host an agent. Public so a page can say so — but
   * nothing in the shop should CHANGE because of it: the agent surface is
   * additive, and a shop that looked different in Chrome would have made the
   * feature detection into a fork.
   */
  get available(): boolean {
    return this.resolve() !== null;
  }

  /**
   * Offers the tools once. Called from the shell, so the whole app has one
   * registration rather than a page-by-page one that races on navigation.
   *
   * Idempotent, because Angular constructs the shell once per app but a hot
   * reload does not respect that, and a double registration is a browser
   * offering the agent two identical tool sets.
   */
  register(tools: readonly ToolDescriptor[]): void {
    if (this.registered) return;

    const context = this.resolve();
    if (!context) return;

    context.provideContext({ tools });
    this.registered = true;
  }

  private resolve(): ModelContext | null {
    if (this.context) return this.context;

    return supportsModelContext() ? (navigator.modelContext ?? null) : null;
  }
}

/**
 * The browser's model context, injectable so a test can supply one.
 *
 * Reaching for `navigator` directly inside the service would make the
 * degradation untestable in exactly the environment that matters — jsdom has no
 * `modelContext`, so every test would take the "not supported" branch and the
 * registration path would never run. The default is null and the service falls
 * back to the real navigator, so nothing has to be provided in the app.
 */
export const MODEL_CONTEXT = new InjectionToken<ModelContext | null>('MODEL_CONTEXT', {
  providedIn: 'root',
  factory: () => null,
});

/** Text back to the agent. The MCP content shape, in one place. */
export function say(text: string): ToolResponse {
  return { content: [{ type: 'text', text }] };
}

/**
 * A refusal, and it is marked as one.
 *
 * `isError` matters more here than in an ordinary API: a model reading "no such
 * SKU" as prose may retry the same call, and reading it as an error will not.
 * The shop saying NO is the interesting half of this project, and it has to
 * survive the trip to an agent.
 */
export function refuse(text: string): ToolResponse {
  return { content: [{ type: 'text', text }], isError: true };
}
