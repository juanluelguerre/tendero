import { computed, inject } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoService, translateObjectSignal } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { CampaignChangeResponse, ManagedCampaignView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { map, type Observable } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { PricingService } from '../../data-access/pricing.service';
import { readStatus } from '../../read-status';
import { type BackofficeAction, BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { countBy, keptCounts, statusChips } from '../../worklist/filter-chips/status-chips';
import { refusalOf } from '../../worklist/refusal';
import { UndoStore } from '../../worklist/undo/undo.store';
import { type CampaignForm, createRequestOf, detailsOf } from './campaign-form';
import { type CampaignBucket, bucketOf, CampaignBuckets, inCulture } from './campaign-state';
import { extendResource } from '@ngrx/signals/resource';

/** What is being done to a campaign: its buttons wait for it. */
export interface CampaignWork {
  readonly code: string;
  readonly what: 'save' | 'schedule' | 'unschedule' | 'end' | 'image';
}

/** The key a new campaign goes by, in the URL and in the work, until the server gives it its code. */
export const NewCampaign = 'new';

/** What the calendar opens on: what is on the shop's windows now. */
const DefaultFilter: CampaignBucket = 'running';

/**
 * The campaign calendar's shared state (ADR 0042, ADR 0047): the filter and the
 * open campaign in the URL; the campaigns, the promotions a campaign can carry
 * and the categories it can be scoped to; and every change — create, save,
 * schedule with Undo, end, upload a picture — which the list and the editor both
 * show. The calendar is read again after each change: the server decides a
 * campaign's phase.
 */
export const CampaignsStore = signalStore(
  withState({
    work: null as CampaignWork | null,
    refusal: null as { readonly code: string; readonly message: string } | null,
  }),
  withProps(() => {
    const route = inject(ActivatedRoute);
    const pricing = inject(PricingService);
    const catalog = inject(CatalogService);
    const culture = inject(CultureStore);
    const campaigns = extendResource(rxResource({ stream: () => pricing.campaigns() }));
    return {
      _router: inject(Router),
      _pricing: pricing,
      _culture: culture,
      _usage: inject(BackofficeUsage),
      _undo: inject(UndoStore),
      _translate: inject(TranslocoService),
      _labels: translateObjectSignal('campaigns.filter'),
      _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
      _campaigns: campaigns,
      ...readStatus(campaigns),
      promotions: extendResource(rxResource({ stream: () => pricing.promotions().pipe(map((list) => list.items)) })),
      categories: extendResource(
        rxResource({
          params: () => culture.active(),
          stream: ({ params }) => catalog.categories(params).pipe(map((list) => list.items)),
        }),
      ),
      /** The counts per chip, from status and phase together, kept while the calendar is read again. */
      _counts: keptCounts(() => {
        const answered = campaigns.value();
        return answered && countBy(answered, bucketOf);
      }),
    };
  }),
  withComputed((store) => {
    const all = computed(() => store._campaigns.value() ?? []);
    const filterKey = computed(() => store._params().get('status') ?? DefaultFilter);
    const selectedCode = computed(() => store._params().get('campaign'));
    const creating = computed(() => selectedCode() === NewCampaign);
    const selected = computed(() => all().find((campaign) => campaign.code === selectedCode()) ?? null);
    return {
      filterKey,
      selectedCode,
      creating,
      selected,
      paneOpen: computed(() => creating() || selected() !== null),
      filters: computed<FilterOption[]>(() =>
        statusChips(CampaignBuckets, store._labels() as Record<string, string>, store._counts()),
      ),
      campaigns: computed(() =>
        filterKey() === 'all' ? all() : all().filter((campaign) => bucketOf(campaign) === filterKey()),
      ),
      categoryNames: computed(
        () => new Map((store.categories.value() ?? []).map((category) => [category.code, category.name])),
      ),
    };
  }),
  withMethods((store) => {
    const navigate = (queryParams: Record<string, string | null>) =>
      void store._router.navigate([], { queryParams, queryParamsHandling: 'merge' });

    const start = (work: CampaignWork) => patchState(store, { work, refusal: null });
    const refused = (code: string, failure: unknown) => {
      patchState(store, { work: null });
      if (isSessionExpired(failure)) return;
      patchState(store, { refusal: { code, message: refusalOf(failure) } });
    };

    /** One change: its campaign's buttons wait, the calendar is read again, the server's sentence when it refuses. */
    const run = (
      work: CampaignWork,
      action: BackofficeAction,
      change: Observable<CampaignChangeResponse>,
      done?: (answer: CampaignChangeResponse) => void,
    ) => {
      start(work);
      change.pipe(store._usage.reported('campaigns', action, 'pane')).subscribe({
        next: (answer) => {
          patchState(store, { work: null });
          done?.(answer);
          store._campaigns.reload();
        },
        error: (failure: unknown) => refused(work.code, failure),
      });
    };

    const move = (campaign: ManagedCampaignView, to: 'schedule' | 'unschedule'): void => {
      const reverse = to === 'schedule' ? 'unschedule' : 'schedule';
      const action: BackofficeAction = to === 'schedule' ? 'campaign_schedule' : 'campaign_unschedule';
      const name = inCulture(campaign.name, store._culture.active());
      run({ code: campaign.code, what: to }, action, store._pricing.moveCampaign(campaign.code, to), (answer) =>
        store._undo.offerTo(store._translate.translate(`campaigns.moved.${to}`, { name }), () =>
          move(answer.campaign, reverse),
        ),
      );
    };

    return {
      filterBy(filter: string): void {
        navigate({ status: filter === DefaultFilter ? null : filter });
      },
      open(campaign: ManagedCampaignView): void {
        navigate({ campaign: campaign.code });
      },
      startNew(): void {
        navigate({ campaign: NewCampaign });
      },
      close(): void {
        navigate({ campaign: null });
      },
      create(form: CampaignForm): void {
        run(
          { code: NewCampaign, what: 'save' },
          'campaign_create',
          store._pricing.createCampaign(createRequestOf(form)),
          (answer) =>
            // The new campaign opens where it now lives: among the drafts.
            navigate({ status: bucketOf(answer.campaign), campaign: answer.campaign.code }),
        );
      },
      update(code: string, form: CampaignForm): void {
        run({ code, what: 'save' }, 'campaign_update', store._pricing.updateCampaign(code, detailsOf(form)));
      },
      move,
      end(campaign: ManagedCampaignView): void {
        run({ code: campaign.code, what: 'end' }, 'campaign_end', store._pricing.moveCampaign(campaign.code, 'end'));
      },
      upload(code: string, file: File, then: (imageId: string) => void): void {
        start({ code, what: 'image' });
        store._pricing.uploadCampaignImage(file).subscribe({
          next: ({ imageId }) => {
            patchState(store, { work: null });
            then(imageId);
          },
          error: (failure: unknown) => refused(code, failure),
        });
      },
    };
  }),
);
