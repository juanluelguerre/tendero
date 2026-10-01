import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CampaignCard } from '../../campaigns/campaign-card/campaign-card';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { ReturnsPolicyStore } from '../../returns/returns-policy.store';
import { AgentCard } from '../../shelves/agent-card/agent-card';
import { ProductRow, type ShelfItem } from '../../shelves/product-row/product-row';
import { PromiseStrip } from '../../shelves/promise-strip/promise-strip';
import { departmentArt } from '../../shop-art';
import { ShopLinks } from '../../shop-links';
import { TrailRow } from '../../trail/trail-row/trail-row';
import { TrailStore } from '../../trail/trail.store';
import { labelled, risingItems, suggestionItems } from './home-bands';
import { HomeModule } from './home-module';
import { HomeStore } from './home.store';

/**
 * The front of the shop, top to bottom (R-8, docs/specs/storefront-home.md; the
 * approved mock-ups): the campaign that leads, *Sigue donde lo dejaste*, *Compra
 * con tu agente*, *Ofertas*, *Puede que te guste*, *Tendencias*, *Lo más
 * vendido*, *Novedades*, the departments and *La tienda promete*.
 *
 * **Each band fails alone.** Every one is its own request, as a resource, and a
 * band whose request failed or answered nothing is simply not drawn — a home
 * that renders nothing because the rankings are down is a shop that closes over
 * a chart. The rows' cards load when they come near the screen (`ProductRow`).
 *
 * **The page composes; the rules are elsewhere**: which campaign leads is the
 * server's order (`leadingCampaign`), what a row says about each product is
 * `home-bands.ts`, what the trail keeps is `TrailStore`'s.
 *
 * It renders inside the search page rather than on a route of its own, because
 * `/es` already IS the search page and the query lives in the URL. The header's
 * box is the shop's one search, so the home no longer asks *¿Qué estás
 * buscando?* — a campaign is a better first sentence than a question the header
 * already asks (the spec).
 */
@Component({
  selector: 'storefront-home-sections',
  imports: [TranslocoDirective, RouterLink, CampaignCard, TrailRow, AgentCard, ProductRow, PromiseStrip, HomeModule],
  providers: [HomeStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home-sections.html',
  host: { class: 'block' },
})
export class HomeSections {
  protected readonly store = inject(HomeStore);
  protected readonly links = inject(ShopLinks);
  protected readonly campaigns = inject(CampaignsStore);
  protected readonly trail = inject(TrailStore);
  protected readonly returnDays = inject(ReturnsPolicyStore).windowDays;

  /** *Porque viste {{name}}*, in the reader's language. */
  protected because(t: (key: string, params?: Record<string, unknown>) => string): (name: string) => string {
    return (name) => t('home.because', { name });
  }

  /** *Porque viste…*: the template hands in the sentence, in the reader's language. */
  protected suggested(because: (name: string) => string): ShelfItem[] {
    const similar = this.store.similar();
    return similar ? suggestionItems(similar, this.store.viewed(), because) : [];
  }

  protected offers(badge: string): ShelfItem[] {
    return labelled(this.store.offerHits(), badge);
  }

  protected trending(rising: string): ShelfItem[] {
    return risingItems(this.store.trendingItems(), rising);
  }

  protected newest(badge: string): ShelfItem[] {
    return labelled(this.store.newestHits(), badge);
  }

  protected art(code: string): string {
    return departmentArt(code);
  }
}
