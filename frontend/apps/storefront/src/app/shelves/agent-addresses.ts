import { inject, Injectable } from '@angular/core';
import { API_BASE_URL } from '@tendero/shared-util';

/**
 * Where an agent reaches the shop: the MCP server and the UCP profile (docs/mcp.md).
 *
 * The API's own origin when one is configured, the page's otherwise — which is
 * the same place in production and, in development, the dev server that
 * forwards `/mcp` and `/.well-known` to the API as it forwards `/api`
 * (`proxy.conf.mjs`). One place, read by the home's card and the page that
 * explains it.
 */
@Injectable({ providedIn: 'root' })
export class AgentAddresses {
  private readonly origin = inject(API_BASE_URL) || globalThis.location?.origin || '';

  readonly mcp = `${this.origin}/mcp`;
  readonly ucp = `${this.origin}/.well-known/ucp`;
}
