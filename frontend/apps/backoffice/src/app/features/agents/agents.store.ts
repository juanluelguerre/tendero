import { computed, inject } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { AgentView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import type { Observable } from 'rxjs';
import { AccountsService, type AgentTrust } from '../../data-access/accounts.service';
import { type ActionVia, type BackofficeAction, BackofficeUsage } from '../../usage';
import { refusalOf } from '../../worklist/refusal';
import { UndoStore } from '../../worklist/undo/undo.store';
import { valueOf } from '@tendero/shared-util';

/** The trust a shopkeeper can give an agent, in the order the selects offer them. */
export const AgentTrusts: readonly AgentTrust[] = ['Known', 'Verified'];

type DraftField = 'agentId' | 'displayName' | 'operator';

const EmptyDraft = { agentId: '', displayName: '', operator: '', trust: 'Known' as AgentTrust };

/**
 * The agent registry's shared state (ADR 0047): the agents, the one the URL has
 * open, the one being registered, and every change — register, trust, suspend,
 * reinstate — which the form, the list and the pane all make and all show. The
 * registry is read again after each change: the server decides what an agent is.
 */
export const AgentsStore = signalStore(
  withState({
    draft: EmptyDraft,
    /** The agent (or `register`) a change is on its way for. */
    busy: null as string | null,
    refusal: null as string | null,
    /** The agent the suspend dialog is asking about, and where it was asked from. */
    suspending: null as { agent: AgentView; via: ActionVia } | null,
  }),
  withProps(() => {
    const route = inject(ActivatedRoute);
    const accounts = inject(AccountsService);
    return {
      _router: inject(Router),
      _accounts: accounts,
      _usage: inject(BackofficeUsage),
      _undo: inject(UndoStore),
      _translate: inject(TranslocoService),
      _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
      agents: rxResource({ stream: () => accounts.agents() }),
    };
  }),
  withComputed((store) => {
    const selectedId = computed(() => store._params().get('agent'));
    return {
      selectedId,
      selected: computed(() => valueOf(store.agents)?.find((agent) => agent.agentId === selectedId()) ?? null),
    };
  }),
  withMethods((store) => {
    const navigate = (agent: string | null) =>
      void store._router.navigate([], { queryParams: { agent }, queryParamsHandling: 'merge' });

    /** One change: busy while it goes, the registry read again when it is done, the server's sentence when it refuses. */
    const run = (
      key: string,
      action: BackofficeAction,
      via: ActionVia,
      change: Observable<unknown>,
      done?: () => void,
    ) => {
      patchState(store, { busy: key, refusal: null });
      change.pipe(store._usage.reported('agents', action, via)).subscribe({
        next: () => {
          patchState(store, { busy: null });
          done?.();
          store.agents.reload();
        },
        error: (failure: unknown) => {
          patchState(store, { busy: null });
          if (isSessionExpired(failure)) return;
          patchState(store, { refusal: refusalOf(failure) });
        },
      });
    };

    const reinstate = (agent: AgentView, via: ActionVia) =>
      run(agent.agentId, 'agent_reinstate', via, store._accounts.reinstate(agent.agentId));

    return {
      open(agent: AgentView): void {
        navigate(agent.agentId);
      },
      close(): void {
        navigate(null);
      },
      edit(field: DraftField, value: string): void {
        patchState(store, (state) => ({ draft: { ...state.draft, [field]: value } }));
      },
      editTrust(value: string): void {
        patchState(store, (state) => ({ draft: { ...state.draft, trust: value as AgentTrust } }));
      },
      register(): void {
        const draft = store.draft();
        run(
          'register',
          'agent_register',
          'page',
          store._accounts.register({ ...draft, agentId: draft.agentId.trim() }),
          () => patchState(store, { draft: EmptyDraft }),
        );
      },
      trust(agent: AgentView, trust: string): void {
        run(agent.agentId, 'agent_trust', 'row', store._accounts.trust(agent.agentId, trust as AgentTrust));
      },
      askToSuspend(agent: AgentView, via: ActionVia): void {
        patchState(store, { suspending: { agent, via } });
      },
      /** Suspending offers Undo, which is reinstating — the reverse on the server, never a local rollback. */
      suspend(reason: string | undefined): void {
        const asked = store.suspending();
        if (!asked || !reason) return;
        const { agent, via } = asked;
        run(agent.agentId, 'agent_suspend', via, store._accounts.suspend(agent.agentId, reason), () =>
          store._undo.offerTo(store._translate.translate('agents.suspended', { agent: agent.displayName }), () =>
            reinstate(agent, via),
          ),
        );
      },
      reinstate,
    };
  }),
);
