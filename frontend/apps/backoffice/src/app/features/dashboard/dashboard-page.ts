import { ChangeDetectionStrategy, Component, computed, effect, type ElementRef, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatTime } from '@tendero/shared-util';
import { filter, map, startWith } from 'rxjs';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { injectDashboardPeriod, queryOfPeriod } from './dashboard-period';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { type DashboardTab, DashboardTabs } from './dashboard-tabs';
import { provideDashboardWidgets } from './dashboard-widget-registry';
import { FullScreen } from './full-screen';

/** A minute, until R-10's live source replaces the clock. */
const WallRefresh = 60_000;

/**
 * The backoffice's home (P11-7), since R-9 an Overview and four tabs (spec,
 * option B): the shop's pulse for the shopkeeper, read from Postgres — never
 * from Prometheus, because the observability stack is a switch and a
 * shopkeeper's home cannot be.
 *
 * The page is the frame: the tabs, each with its URL, and one period for all of
 * them, in the URL too (`?period=week`), so a monitor keeps both. Comparing a
 * week's sales with a day's refusals is a mistake the interface should not make
 * easy. What each tab shows is data (`DashboardLayouts`), drawn by the grid from
 * the widgets registered here; the reads they share live and die with the page.
 *
 * Each tab can go full screen on a monitor of its own: the browser's API on this
 * page's own section, so there is no kiosk mode, no second identity and no token
 * in a URL — and while it is up, the figures are asked for again every minute.
 */
@Component({
  selector: 'backoffice-dashboard',
  imports: [TranslocoDirective, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.css',
  providers: [DashboardSources, DashboardRefresh, provideDashboardWidgets()],
})
export class DashboardPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);
  private readonly wall = viewChild.required<ElementRef<HTMLElement>>('wall');

  protected readonly fullScreen = inject(FullScreen);
  protected readonly periods: readonly DashboardPeriod[] = ['Day', 'Week', 'Month'];
  protected readonly tabs = DashboardTabs;
  protected readonly period = injectDashboardPeriod();
  protected readonly queryOfPeriod = queryOfPeriod;

  /** The tab on screen, for the wall's head: the child route's own data. */
  protected readonly tab = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => (this.route.snapshot.firstChild?.data['tab'] as DashboardTab | undefined) ?? 'overview'),
    ),
    { initialValue: 'overview' as DashboardTab },
  );

  protected readonly updated = computed(() => formatTime(new Date(this.refresh.at()), this.culture.active()));

  constructor() {
    // On the wall the figures are asked for again every minute; off it, never on a clock.
    effect(() => (this.fullScreen.active() ? this.refresh.every(WallRefresh) : this.refresh.stop()));
  }

  protected linkOf(tab: DashboardTab): string[] {
    return tab === 'overview' ? ['/dashboard'] : ['/dashboard', tab];
  }

  protected choose(period: DashboardPeriod): void {
    void this.router.navigate([], { relativeTo: this.route.firstChild ?? this.route, queryParams: { period: queryOfPeriod(period) }, queryParamsHandling: 'merge' });
  }

  protected enterWall(): void {
    void this.fullScreen.enter(this.wall().nativeElement);
  }

  protected leaveWall(): void {
    void this.fullScreen.leave();
  }
}
