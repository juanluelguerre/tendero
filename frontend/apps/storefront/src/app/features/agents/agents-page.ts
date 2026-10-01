import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CopyText } from '@tendero/shared-ui';
import { AgentAddresses } from '../../shelves/agent-addresses';

/**
 * *Compra con tu agente*, the page behind the home's card (R-8, H-5): docs/mcp.md
 * for a shopper rather than a developer. What an assistant can do here, the three
 * ways one reaches the shop — the MCP server, the browser's own agent (WebMCP),
 * and UCP for an agent that buys on somebody's behalf — and that the person stays
 * the one who pays.
 *
 * A page and not a dialog: it has an address to share, and the shop's
 * accessibility ratchet reads it like any other page.
 */
@Component({
  selector: 'storefront-agents-page',
  imports: [TranslocoDirective, CopyText],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-page.html',
  host: { class: 'block' },
})
export class AgentsPage {
  protected readonly addresses = inject(AgentAddresses);

  /** The one command a Claude Code user types, with the shop's own address in it. */
  protected readonly command = `claude mcp add --transport http tendero ${this.addresses.mcp}`;
}
