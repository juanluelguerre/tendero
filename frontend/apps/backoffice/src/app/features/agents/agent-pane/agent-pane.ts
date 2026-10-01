import { Component, inject, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { CopyText } from '@tendero/shared-ui';
import { formatDate } from '@tendero/shared-util';
import { WorklistPane } from '../../../worklist/worklist-pane/worklist-pane';
import { AgentStatus } from '../agent-status/agent-status';
import { AgentsStore } from '../agents.store';

/** The open agent beside the registry: who it is, who vouched for it, and Suspend or Reinstate. */
@Component({
  selector: 'backoffice-agent-pane',
  imports: [TranslocoDirective, WorklistPane, AgentStatus, CopyText],
  templateUrl: './agent-pane.html',
  // The pane is laid out by the page's list-and-pane grid, as if it were its child.
  host: { class: 'contents' },
})
export class AgentPane {
  protected readonly store = inject(AgentsStore);
  private readonly culture = inject(CultureStore);

  /** The person closed the pane. */
  readonly closed = output<void>();

  protected when(at: string): string {
    return formatDate(at, this.culture.active());
  }
}
