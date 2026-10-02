import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProposalCardStore } from './proposal-card.store';

/**
 * A turn's proposals as they arrive in the panel (spec, *The panel*): how many
 * and of what kind, the reason, three of them *now → proposed* and how many
 * more, **Review** — the clay action an agent's work always wears — and the
 * promise that they are kept in Proposals whatever happens to the panel.
 */
@Component({
  selector: 'backoffice-proposal-card',
  imports: [TranslocoDirective, RouterLink],
  templateUrl: './proposal-card.html',
  host: { class: 'block' },
  providers: [ProposalCardStore],
})
export class ProposalCard {
  protected readonly store = inject(ProposalCardStore);

  /** The proposals the turn stored. */
  readonly ids = input.required<readonly string[]>();
  /** How many times the panel's review decided something: the card reads its batch again. */
  readonly revision = input(0);

  /** The person wants to review the batch. */
  readonly review = output<readonly string[]>();

  constructor() {
    this.store.follow(computed(() => ({ ids: this.ids(), revision: this.revision() })));
  }
}
