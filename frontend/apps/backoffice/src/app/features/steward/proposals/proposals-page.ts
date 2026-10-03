import { Component, inject } from '@angular/core';
import { BackofficeUsage } from '../../../usage';
import { landing, stepOf } from '../../../worklist/list-keys';
import { ProposalList } from './proposal-list/proposal-list';
import { ProposalPane } from './proposal-pane/proposal-pane';
import { ProposalsStore } from './proposals.store';
import { SidePaneContent } from '../../../shell/side-pane/side-pane';

/**
 * What Steward proposed (phase 12), as the lists of work are drawn: a filter by
 * what became of each proposal, the open one beside the list with its change,
 * its reason and where it came from, and Approve or Reject on it. Nothing here
 * has changed the shop until a person approves it (invariant 12), and what a
 * panel proposed is never lost when the panel closes: it waits here.
 */
@Component({
  selector: 'backoffice-proposals-page',
  imports: [SidePaneContent, ProposalList, ProposalPane],
  templateUrl: './proposals-page.html',
  host: { class: 'block', '(document:keydown)': 'key($event)' },
  providers: [ProposalsStore],
})
export class ProposalsPage {
  protected readonly store = inject(ProposalsStore);
  private readonly usage = inject(BackofficeUsage);

  /** j and k move down and up the proposals, the pane following. */
  protected key(event: KeyboardEvent): void {
    const step = stepOf(event, !!document.querySelector('dialog[open]'));
    if (!step) return;
    const proposals = this.store.proposals();
    const index = landing(
      proposals.findIndex((proposal) => proposal.id === this.store.selectedId()),
      proposals.length,
      step,
    );
    if (index === null) return;
    event.preventDefault();
    this.usage.affordance('keys', step === 1 ? 'next' : 'previous');
    this.store.open(proposals[index]);
  }
}
