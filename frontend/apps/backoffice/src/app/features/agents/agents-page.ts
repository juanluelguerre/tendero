import { ChangeDetectionStrategy, Component, effect, inject, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ConfirmDialog } from '../../worklist/confirm-dialog/confirm-dialog';
import { AgentList } from './agent-list/agent-list';
import { AgentPane } from './agent-pane/agent-pane';
import { AgentRegister } from './agent-register/agent-register';
import { AgentsStore } from './agents.store';

/**
 * The agents a shop vouches for (P11-6), as R-3 drew the lists of work (R-4.3).
 *
 * Suspending asks for a reason, because the domain keeps one and the agent is
 * told it; it does **not** ask "are you sure?", because it can be undone —
 * reinstating is the domain's own reverse, audited like any other change, and
 * it is offered at once (R-2, P12). The job it serves (R-0, B5) keeps its four
 * interactions, and gains a way back from a wrong one.
 */
@Component({
  selector: 'backoffice-agents',
  imports: [TranslocoDirective, ConfirmDialog, AgentList, AgentRegister, AgentPane],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-page.html',
  // The page is a frame: the list-and-pane layout is shared by every worklist
  // (styles.css), and what the list, the form and the pane have of their own is in their
  // folders.
  host: { class: 'block' },
  providers: [AgentsStore],
})
export class AgentsPage {
  protected readonly store = inject(AgentsStore);
  private readonly suspendDialog = viewChild(ConfirmDialog);

  constructor() {
    // The row and the pane ask to suspend through the store; the dialog is the page's.
    effect(() => {
      if (this.store.suspending()) queueMicrotask(() => this.suspendDialog()?.open());
    });
  }
}
