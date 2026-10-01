import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ListState } from '../../../worklist/list-state/list-state';
import { AgentStatus } from '../agent-status/agent-status';
import { AgentsStore, AgentTrusts } from '../agents.store';
import { valueOf } from '@tendero/shared-util';

/**
 * The registered agents (P11-2): each with its operator, its trust — changed in
 * place — and whether it may act, with Suspend or Reinstate on the row. It reads
 * and acts through the page's store, which the form and the pane share.
 */
@Component({
  selector: 'backoffice-agent-list',
  imports: [TranslocoDirective, AgentStatus, ListState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-list.html',
})
export class AgentList {
  /** A resource read without throwing when its request failed (Angular 22). */
  protected readonly valueOf = valueOf;

  protected readonly store = inject(AgentsStore);
  protected readonly trusts = AgentTrusts;
}
