import { ChangeDetectionStrategy, Component, computed, type ElementRef, inject, signal, viewChild } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { debounceTime, distinctUntilChanged, map, of } from 'rxjs';
import { AccountsService } from '../data-access/accounts.service';
import { CatalogService } from '../data-access/catalog.service';
import { OrderingService } from '../data-access/ordering.service';
import { BackofficeUsage } from '../usage';
import { Navigation } from './navigation';
import { looksLikeId, matches, type PaletteOption, step } from './palette-search';

/** Enough to choose from; the list is one Enter away for more. */
const PerKind = 5;

/** Below this, a record search would match half the shop. Screens still answer. */
const RecordsFrom = 2;

/**
 * The command palette (the redesign's R-3, P2): Ctrl K, ⌘K or /, then a screen
 * by its name in either language, or an order, a product or an agent by what
 * the shopkeeper remembers of it. Enter goes there with its pane open.
 *
 * A native modal dialog holding the WAI-ARIA combobox pattern: the input keeps
 * the focus, the arrows move the active option, and the platform traps the
 * focus and closes on Esc. It only takes somebody somewhere; the action is
 * done in the pane, where its words and its confirmation are.
 */
@Component({
  selector: 'backoffice-palette',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './palette.html',
  styleUrl: './palette.css',
})
export class Palette {
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly culture = inject(CultureStore);
  private readonly ordering = inject(OrderingService);
  private readonly catalog = inject(CatalogService);
  private readonly accounts = inject(AccountsService);
  private readonly usage = inject(BackofficeUsage);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  protected readonly query = signal('');
  private readonly asked = toSignal(toObservable(this.query).pipe(debounceTime(200), distinctUntilChanged()), { initialValue: '' });
  protected readonly active = signal(0);
  private readonly isOpen = signal(false);

  private readonly records = rxResource({
    params: () => (this.isOpen() && this.asked().trim().length >= RecordsFrom ? { q: this.asked().trim(), culture: this.culture.active() } : undefined),
    stream: ({ params }) =>
      this.ordering.orders(null, params.q).pipe(
        map((result) => result.orders.slice(0, PerKind)),
        map((orders) => ({ orders })),
      ),
  });

  private readonly products = rxResource({
    params: () => (this.isOpen() && this.asked().trim().length >= RecordsFrom ? { q: this.asked().trim(), culture: this.culture.active() } : undefined),
    stream: ({ params }) => this.catalog.list(null, params.culture, 1, PerKind, params.q),
  });

  /** A laboratory's handful: read once when the palette first opens, and matched here. */
  private readonly agents = rxResource({
    params: () => (this.isOpen() ? true : undefined),
    stream: ({ params }) => (params ? this.accounts.agents() : of([])),
  });

  protected readonly options = computed<PaletteOption[]>(() => {
    const query = this.query().trim();
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    this.culture.active();

    const screens: PaletteOption[] = Navigation.flatMap((group) =>
      group.items.map((item) => ({
        id: `screen-${item.label}`,
        kind: 'screen' as const,
        label: t(`nav.${item.label}`),
        detail: t(`nav.groups.${group.label}`),
        route: item.route,
        queryParams: {},
      })),
    ).filter((option) => matches(`${option.label} ${option.detail}`, query));

    if (query.length < RecordsFrom) return screens;

    const orders: PaletteOption[] = (this.records.value()?.orders ?? []).map((order) => ({
      id: `order-${order.orderId}`,
      kind: 'order',
      label: t('palette.order', { id: order.orderId.split('-')[0] }),
      detail: `${order.recipientName} · ${order.city} · ${t('orders.status.' + order.status)}`,
      route: '/orders',
      queryParams: { status: 'all', order: order.orderId },
    }));

    const products: PaletteOption[] = (this.products.value()?.items ?? []).map((product) => ({
      id: `product-${product.productId}`,
      kind: 'product',
      label: product.name,
      detail: `${product.code} · ${t('catalogue.status.' + product.status)}`,
      route: '/catalogue',
      queryParams: { status: 'all', product: product.code },
    }));

    const agents: PaletteOption[] = (this.agents.value() ?? [])
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
    return looksLikeId(query) ? [...orders, ...products, ...agents, ...screens] : [...screens, ...orders, ...products, ...agents];
  });

  protected readonly loading = computed(() => this.records.isLoading() || this.products.isLoading());

  open(): void {
    if (this.isOpen()) return;
    this.query.set('');
    this.active.set(0);
    this.isOpen.set(true);
    this.dialog().nativeElement.showModal();
    this.input().nativeElement.focus();
  }

  protected closed(): void {
    this.isOpen.set(false);
  }

  protected typed(value: string): void {
    this.query.set(value);
    this.active.set(0);
  }

  protected key(event: KeyboardEvent): void {
    const count = this.options().length;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.active.set(step(this.active(), count, event.key === 'ArrowDown' ? 1 : -1));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const option = this.options()[this.active()];
      if (option) this.go(option);
    }
  }

  protected go(option: PaletteOption): void {
    this.usage.affordance('palette', option.kind);
    this.dialog().nativeElement.close();
    void this.router.navigate([option.route], { queryParams: option.queryParams });
  }
}
