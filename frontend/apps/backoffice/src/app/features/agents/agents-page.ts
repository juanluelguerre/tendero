import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { AgentView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDate } from '@tendero/shared-util';
import type { Observable } from 'rxjs';
import { AccountsService, type AgentTrust } from '../../data-access/accounts.service';
import { type ActionVia, BackofficeUsage, type BackofficeAction } from '../../usage';
import { ConfirmDialog } from '../../work/confirm-dialog';
import { Undo } from '../../work/undo';
import { WorkPane } from '../../work/work-pane';

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
  imports: [TranslocoDirective, WorkPane, ConfirmDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-page.html',
  styleUrl: './agents-page.css',
})
export class AgentsPage {
  private readonly accounts = inject(AccountsService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);
  private readonly undo = inject(Undo);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly suspendDialog = viewChild(ConfirmDialog);
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  protected readonly agents = rxResource({ stream: () => this.accounts.agents() });
  protected readonly selectedId = computed(() => this.params().get('agent'));
  protected readonly selected = computed(() => this.agents.value()?.find((agent) => agent.agentId === this.selectedId()) ?? null);

  protected readonly trusts: readonly AgentTrust[] = ['Known', 'Verified'];

  /** The new agent's fields, and the server's words when it says no. */
  protected readonly draft = signal({ agentId: '', displayName: '', operator: '', trust: 'Known' as AgentTrust });
  protected readonly refusal = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);

  /** The agent the suspension dialog is about. */
  protected readonly suspending = signal<{ agent: AgentView; via: ActionVia } | null>(null);

  /**
   * The trust as the screen names it. The API says it in lower case and the
   * options and translations use the enum's name, so "verified" matched no
   * option and the select showed every agent as Known.
   */
  protected trustOf(agent: AgentView): AgentTrust {
    return (agent.trust.charAt(0).toUpperCase() + agent.trust.slice(1)) as AgentTrust;
  }

  protected when(at: string): string {
    return formatDate(at, this.culture.active());
  }

  protected open(agent: AgentView): void {
    void this.router.navigate([], { queryParams: { agent: agent.agentId }, queryParamsHandling: 'merge' });
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { agent: null }, queryParamsHandling: 'merge' });
  }

  protected edit(field: 'agentId' | 'displayName' | 'operator', value: string): void {
    this.draft.update((draft) => ({ ...draft, [field]: value }));
  }

  protected editTrust(value: string): void {
    this.draft.update((draft) => ({ ...draft, trust: value as AgentTrust }));
  }

  protected register(): void {
    const draft = this.draft();
    this.run('register', 'agent_register', 'page', this.accounts.register({ ...draft, agentId: draft.agentId.trim() }), () =>
      this.draft.set({ agentId: '', displayName: '', operator: '', trust: 'Known' }),
    );
  }

  protected trust(agent: AgentView, trust: string): void {
    this.run(agent.agentId, 'agent_trust', 'row', this.accounts.trust(agent.agentId, trust as AgentTrust));
  }

  protected askToSuspend(agent: AgentView, via: ActionVia): void {
    this.suspending.set({ agent, via });
    // After the change detection that gives the dialog this agent's words.
    queueMicrotask(() => this.suspendDialog()?.open());
  }

  protected suspend(reason: string | undefined): void {
    const asked = this.suspending();
    if (!asked || !reason) return;
    const { agent, via } = asked;
    this.run(agent.agentId, 'agent_suspend', via, this.accounts.suspend(agent.agentId, reason), () =>
      this.undo.offerTo(this.transloco.translate('agents.suspended', { agent: agent.displayName }), () => this.reinstate(agent, via)),
    );
  }

  protected reinstate(agent: AgentView, via: ActionVia): void {
    this.run(agent.agentId, 'agent_reinstate', via, this.accounts.reinstate(agent.agentId));
  }

  /** One change at a time, the list asked again after it, and a refusal in the server's words. */
  private run(key: string, action: BackofficeAction, via: ActionVia, change: Observable<unknown>, done?: () => void): void {
    this.busy.set(key);
    this.refusal.set(null);

    change.pipe(this.usage.reported('agents', action, via)).subscribe({
      next: () => {
        this.busy.set(null);
        done?.();
        this.agents.reload();
      },
      error: (failure: unknown) => {
        this.busy.set(null);
        if (isSessionExpired(failure)) return;
        this.refusal.set(failure instanceof HttpErrorResponse && typeof failure.error === 'string' ? failure.error : '');
      },
    });
  }
}
