import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { DateTimePipe } from '@tendero/shared-ui';
import { FilterChips } from '../../../../worklist/filter-chips/filter-chips';
import { ListState } from '../../../../worklist/list-state/list-state';
import { Pager } from '../../../../worklist/pager/pager';
import { ProposalsStore } from '../proposals.store';

/**
 * Steward's proposals, the newest first: the product, the action in words, what
 * it proposes and when. Decisions are taken in the pane, where the change, the
 * reason and the provenance are in view — one clay action per view.
 */
@Component({
  selector: 'backoffice-proposal-list',
  imports: [TranslocoDirective, FilterChips, Pager, ListState, DateTimePipe],
  templateUrl: './proposal-list.html',
})
export class ProposalList {
  protected readonly store = inject(ProposalsStore);
  protected readonly culture = inject(CultureStore);
  protected readonly pageSize = this.store.pageSize;
}
