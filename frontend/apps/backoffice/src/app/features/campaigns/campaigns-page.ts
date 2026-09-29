import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { countBy, keptCounts, statusChips } from '../../work/status-chips';
import { WorkPane } from '../../work/work-pane';
import { CampaignCalendar, NewCampaign } from './campaign-calendar';
import { CampaignEditor } from './campaign-editor';
import { bucketOf, CampaignBuckets, type CampaignBucket, inCulture, windowOf } from './campaign-state';

/** What the list opens on: what the shop is showing now. */
const DefaultFilter: CampaignBucket = 'running';

/**
 * The shop's campaigns (R-8, H-1b) — named windows over promotions, set here
 * for concrete dates (ADR 0044) — as ADR 0042's list and pane.
 *
 * The chips are the calendar a shopkeeper asks about: what runs now, what is
 * scheduled, what is still a draft, what is over. They are derived from the
 * stored status and the window's phase together (`bucketOf`), because neither
 * alone says which. The filter and the open campaign live in the URL, as every
 * list's do, so the palette and a link can open it exactly there.
 */
@Component({
  selector: 'backoffice-campaigns-page',
  imports: [TranslocoDirective, NgTemplateOutlet, WorkPane, FilterChips, CampaignEditor],
  providers: [CampaignCalendar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './campaigns-page.html',
  styleUrl: './campaigns-page.css',
})
export class CampaignsPage {
  protected readonly calendar = inject(CampaignCalendar);
  private readonly culture = inject(CultureStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly filterLabels = translateObjectSignal('campaigns.filter');
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  protected readonly filterKey = computed(() => this.params().get('status') ?? DefaultFilter);
  protected readonly selectedCode = computed(() => this.params().get('campaign'));
  protected readonly creating = computed(() => this.selectedCode() === NewCampaign);

  private readonly all = computed(() => this.calendar.campaigns.value() ?? []);
  protected readonly loading = computed(() => this.calendar.campaigns.isLoading() && !this.calendar.campaigns.hasValue());
  protected readonly failed = computed(() => this.calendar.campaigns.status() === 'error');

  private readonly counts = keptCounts(() => {
    const campaigns = this.calendar.campaigns.value();
    return campaigns && countBy(campaigns, bucketOf);
  });

  protected readonly filters = computed<FilterOption[]>(() =>
    statusChips(CampaignBuckets, this.filterLabels() as Record<string, string>, this.counts()),
  );

  protected readonly campaigns = computed(() => {
    const filter = this.filterKey();
    return filter === 'all' ? this.all() : this.all().filter((campaign) => bucketOf(campaign) === filter);
  });

  /** From every campaign, not only the filter's: one that is scheduled from the pane stays open as it leaves the drafts. */
  protected readonly selected = computed(() => this.all().find((campaign) => campaign.code === this.selectedCode()) ?? null);
  protected readonly paneOpen = computed(() => this.creating() || this.selected() !== null);

  /** The pane's heading: the open campaign's name in the reader's language, or null for a new one. */
  protected readonly selectedName = computed(() => {
    const selected = this.selected();
    return selected ? inCulture(selected.name, this.culture.active()) : null;
  });

  protected nameOf(campaign: ManagedCampaignView): string {
    return inCulture(campaign.name, this.culture.active());
  }

  /** The category's name, or null for the whole shop. */
  protected categoryOf(campaign: ManagedCampaignView): string | null {
    if (campaign.scope !== 'Category' || !campaign.categoryCode) return null;
    return this.calendar.categoryNames().get(campaign.categoryCode) ?? campaign.categoryCode;
  }

  protected windowOf(campaign: ManagedCampaignView): string {
    return windowOf(campaign.startsAt, campaign.endsAt, this.culture.active());
  }

  protected bucketOf(campaign: ManagedCampaignView): CampaignBucket {
    return bucketOf(campaign);
  }

  protected filterBy(filter: string): void {
    void this.router.navigate([], { queryParams: { status: filter === DefaultFilter ? null : filter }, queryParamsHandling: 'merge' });
  }

  protected open(campaign: ManagedCampaignView): void {
    void this.router.navigate([], { queryParams: { campaign: campaign.code }, queryParamsHandling: 'merge' });
  }

  protected startNew(): void {
    void this.router.navigate([], { queryParams: { campaign: NewCampaign }, queryParamsHandling: 'merge' });
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { campaign: null }, queryParamsHandling: 'merge' });
  }
}
