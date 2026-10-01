import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CopyText, Icon } from '@tendero/shared-ui';
import { ShopLinks } from '../../shop-links';
import { AgentAddresses } from '../agent-addresses';

/**
 * *Compra con tu agente* (R-8, H-5; the author, 2026-09-29): what an assistant
 * can do here, the MCP server's address to point one at, and the way to the page
 * that explains the rest. The shopper stays the one who pays, and the card says
 * so in its first sentence.
 *
 * Quiet on purpose — the accent's tint, no clay button: the campaign above has
 * the view's one action, and this is an invitation, not the shop's main call.
 */
@Component({
  selector: 'storefront-agent-card',
  imports: [TranslocoDirective, RouterLink, CopyText, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-card.html',
  styleUrl: './agent-card.css',
})
export class AgentCard {
  protected readonly links = inject(ShopLinks);
  protected readonly mcp = inject(AgentAddresses).mcp;

  /** Where *Cómo conectarlo* goes: the page that summarises docs/mcp.md. */
  protected readonly howTo = computed(() => this.links.agents());
}
