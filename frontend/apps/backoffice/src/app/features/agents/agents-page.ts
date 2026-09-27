import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AgentView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDate } from '@tendero/shared-util';
import type { Observable } from 'rxjs';
import { AccountsService, type AgentTrust } from '../../data-access/accounts.service';
import { BackofficeUsage, type BackofficeAction } from '../../usage';

/**
 * The agents the shop knows (P11-2's registry, its screen in P11-7): who operates
 * each, how far the shop trusts it, and whether it is suspended — with the reason,
 * because a suspension nobody can explain is one nobody can lift.
 *
 * An agent nobody registered is not listed and not refused: it shops as
 * *untrusted*, under the shop's tighter caps (P11-6). Registering is how a
 * shopkeeper vouches for one. The server owns every rule; a refusal is shown in
 * the server's own words.
 */
@Component({
  selector: 'backoffice-agents',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-page.html',
  styleUrl: './agents-page.css',
})
export class AgentsPage {
  private readonly accounts = inject(AccountsService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);

  protected readonly agents = rxResource({ stream: () => this.accounts.agents() });

  protected readonly trusts: readonly AgentTrust[] = ['Known', 'Verified'];

  /** The new agent's fields, and the server's words when it says no. */
  protected readonly draft = signal({ agentId: '', displayName: '', operator: '', trust: 'Known' as AgentTrust });
  protected readonly refusal = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);

  /** Which row is asking for a suspension reason. One at a time. */
  protected readonly suspending = signal<string | null>(null);
  protected readonly reason = signal('');

  protected when(at: string): string {
    return formatDate(at, this.culture.active());
  }

  protected edit(field: 'agentId' | 'displayName' | 'operator', value: string): void {
    this.draft.update((draft) => ({ ...draft, [field]: value }));
  }

  protected editTrust(value: string): void {
    this.draft.update((draft) => ({ ...draft, trust: value as AgentTrust }));
  }

  protected register(): void {
    const draft = this.draft();
    this.run('register', 'agent_register', this.accounts.register({ ...draft, agentId: draft.agentId.trim() }), () =>
      this.draft.set({ agentId: '', displayName: '', operator: '', trust: 'Known' }),
    );
  }

  protected trust(agent: AgentView, trust: string): void {
    this.run(agent.agentId, 'agent_trust', this.accounts.trust(agent.agentId, trust as AgentTrust));
  }

  protected askReason(agent: AgentView): void {
    this.suspending.set(agent.agentId);
    this.reason.set('');
  }

  protected suspend(agent: AgentView): void {
    this.run(agent.agentId, 'agent_suspend', this.accounts.suspend(agent.agentId, this.reason().trim()), () => this.suspending.set(null));
  }

  protected reinstate(agent: AgentView): void {
    this.run(agent.agentId, 'agent_reinstate', this.accounts.reinstate(agent.agentId));
  }

  /** One change at a time, the list reloaded after it, and a refusal in the server's words. */
  private run(key: string, action: BackofficeAction, change: Observable<unknown>, done?: () => void): void {
    this.busy.set(key);
    this.refusal.set(null);

    change.pipe(this.usage.reported('agents', action)).subscribe({
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
