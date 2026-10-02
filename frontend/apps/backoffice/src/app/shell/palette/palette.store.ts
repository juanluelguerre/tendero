import { computed, inject } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { debounceTime, distinctUntilChanged, map, of } from 'rxjs';
import { AccountsService } from '../../data-access/accounts.service';
import { CatalogService } from '../../data-access/catalog.service';
import { OrderingService } from '../../data-access/ordering.service';
import { Navigation } from '../nav/navigation';
import { orderReference } from '@tendero/shared-util';
import { looksLikeId, matches, type PaletteOption, step } from './palette-search';
import { extendResource } from '@ngrx/signals/resource';

/** Enough to choose from; the list is one Enter away for more. */
const PerKind = 5;

/** Below this, a record search would match half the shop. Screens still answer. */
const RecordsFrom = 2;

/**
 * The command palette's state (ADR 0047): what is typed, what it finds — a
 * screen by its name in either language, or an order, a product or an agent by
 * what the shopkeeper remembers of it — and which option is active. Nothing is
 * asked while it is closed.
 */
export const PaletteStore = signalStore(
  withState({ query: '', active: 0, isOpen: false }),
  withProps((store) => {
    const culture = inject(CultureStore);
    const ordering = inject(OrderingService);
    const catalog = inject(CatalogService);
    const accounts = inject(AccountsService);
    const asked = toSignal(toObservable(store.query).pipe(debounceTime(200), distinctUntilChanged()), {
      initialValue: '',
    });
    const search = () =>
      store.isOpen() && asked().trim().length >= RecordsFrom
        ? { q: asked().trim(), culture: culture.active() }
        : undefined;

    return {
      _culture: culture,
      _translate: inject(TranslocoService),
      _orders: extendResource(
        rxResource({
          params: search,
          stream: ({ params }) =>
            ordering.orders(null, params.q).pipe(map((result) => result.orders.slice(0, PerKind))),
        }),
      ),
      _products: extendResource(
        rxResource({
          params: search,
          stream: ({ params }) => catalog.list(null, params.culture, 1, PerKind, params.q),
        }),
      ),
      /** A laboratory's handful: read once when the palette first opens, and matched here. */
      _agents: extendResource(
        rxResource({
          params: () => (store.isOpen() ? true : undefined),
          stream: ({ params }) => (params ? accounts.agents() : of([])),
        }),
      ),
    };
  }),
  withComputed((store) => ({
    options: computed<PaletteOption[]>(() => {
      const query = store.query().trim();
      const t = (key: string, params?: Record<string, unknown>) => store._translate.translate(key, params);
      store._culture.active();

      const ask: PaletteOption = {
        id: 'ask-steward',
        kind: 'steward',
        label: t('palette.ask'),
        detail: t('palette.askDetail'),
        route: '',
        queryParams: {},
      };
      const screens: PaletteOption[] = [
        ask,
        ...Navigation.flatMap((group) =>
          group.items.map((item) => ({
            id: `screen-${item.label}`,
            kind: 'screen' as const,
            label: t(`nav.${item.label}`),
            detail: t(`nav.groups.${group.label}`),
            route: item.route,
            queryParams: {},
          })),
        ),
      ].filter((option) => matches(`${option.label} ${option.detail}`, query));

      if (query.length < RecordsFrom) return screens;

      const orders: PaletteOption[] = (store._orders.value() ?? []).map((order) => ({
        id: `order-${order.orderId}`,
        kind: 'order',
        label: t('palette.order', { id: orderReference(order.orderId) }),
        detail: `${order.recipientName} · ${order.city} · ${t('orders.status.' + order.status)}`,
        route: '/orders',
        queryParams: { status: 'all', order: order.orderId },
      }));

      const products: PaletteOption[] = (store._products.value()?.items ?? []).map((product) => ({
        id: `product-${product.productId}`,
        kind: 'product',
        label: product.name,
        detail: `${product.code} · ${t('catalogue.status.' + product.status)}`,
        route: '/catalogue',
        queryParams: { status: 'all', product: product.code },
      }));

      const agents: PaletteOption[] = (store._agents.value() ?? [])
        .filter((agent) => matches(`${agent.displayName} ${agent.agentId}`, query))
        .slice(0, PerKind)
        .map((agent) => ({
          id: `agent-${agent.agentId}`,
          kind: 'agent',
          label: agent.displayName,
          detail: `${agent.operator} · ${agent.agentId}`,
          route: '/agents',
          queryParams: { agent: agent.agentId },
        }));

      // An id is what nobody types by accident: then orders lead.
      return looksLikeId(query)
        ? [...orders, ...products, ...agents, ...screens]
        : [...screens, ...orders, ...products, ...agents];
    }),
    loading: computed(() => store._orders.isLoading() || store._products.isLoading()),
  })),
  withMethods((store) => ({
    /** Opened afresh: an empty search, the first option active. */
    opened(): void {
      patchState(store, { query: '', active: 0, isOpen: true });
    },
    closed(): void {
      patchState(store, { isOpen: false });
    },
    /** A new search starts at the top. */
    typed(query: string): void {
      patchState(store, { query, active: 0 });
    },
    /** The pointer makes the option under it the active one. */
    activate(index: number): void {
      patchState(store, { active: index });
    },
    /** The arrows move the active option, wrapping around the options there are. */
    step(by: 1 | -1): void {
      patchState(store, (state) => ({ active: step(state.active, store.options().length, by) }));
    },
  })),
);
