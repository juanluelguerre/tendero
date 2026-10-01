import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { refuse, say, type ToolDescriptor } from '@tendero/shared-agent';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { CampaignService } from '../data-access/campaign.service';
import { CartStore } from '../cart/cart.store';
import { ProductSearchService } from '../data-access/product-search.service';
import { ProductService } from '../data-access/product.service';
import { ActiveListing } from '../listing/active-listing.store';
import { describeCampaigns } from './campaign-tool';
import { chooseFilter, describeFacets, type FilterChoice } from './filter-tool';

/**
 * Everything the shop offers a browser agent, in ONE file.
 *
 * That is deliberate and it is `P6-4`: WebMCP is under incubation at the W3C Web
 * Machine Learning Community Group and Chrome-only, so the specification WILL
 * move. Keeping the descriptors together means a change to the shape of a tool
 * is one edit in one place rather than a search across the app — and it makes
 * the surface readable, which matters because this list IS the contract with
 * every agent that visits.
 *
 * **Every tool calls the same service the interface calls** (`P6-3`). Not a
 * parallel HTTP path, not a second copy of a rule. Adding to the cart through an
 * agent and pressing the button reach `CartStore.add` identically, so the price
 * revalidation, the token handling and the error shape cannot drift between
 * them. An eslint boundary keeps this library from importing `HttpClient`
 * directly, which is the mechanical half of the same rule.
 *
 * **The agent is the shopper.** It runs in their tab, so it already has their
 * cart token, their language and their session — there is no principal to
 * establish and no mandate to verify. That is what makes this phase small and
 * phase 11 large, and it is the distinction worth writing about.
 */
@Injectable({ providedIn: 'root' })
export class StorefrontTools {
  private readonly search = inject(ProductSearchService);
  private readonly products = inject(ProductService);
  private readonly cart = inject(CartStore);
  private readonly culture = inject(CultureStore);
  private readonly listing = inject(ActiveListing);
  private readonly campaigns = inject(CampaignService);

  /** Enough to answer with, few enough not to fill a model's context. */
  private static readonly MaximumResults = 8;

  all(): readonly ToolDescriptor[] {
    return [
      this.searchProducts(),
      this.listFilters(),
      this.applyFilter(),
      this.getProduct(),
      this.addToCart(),
      this.getCart(),
      this.listCampaigns(),
    ];
  }

  private searchProducts(): ToolDescriptor {
    return {
      name: 'search_products',
      description:
        'Searches the shop and returns matching products with their price range, ' +
        'stock and a link. Use it to find what the shopper is asking for. It ' +
        'searches in the language the page is in and does not translate the query.',
      inputSchema: {
        type: 'object',
        properties: {
          query: { type: 'string', description: 'What to look for, in the page language.' },
        },
        required: ['query'],
      },
      execute: async (input) => {
        const query = String(input['query'] ?? '').trim();

        // The same floor the search box enforces. An agent that sent one
        // character would get noise back and could not tell it from an answer.
        if (query.length < 2) return refuse('A query needs at least two characters.');

        const page = await firstValueFrom(this.search.search(query, this.culture.active()));

        if (page.total === 0) return say(`Nothing matches "${query}".`);

        const lines = page.hits.slice(0, StorefrontTools.MaximumResults).map((hit) => {
          const price =
            hit.priceFrom < hit.priceTo
              ? `${this.money(hit.priceFrom, hit.priceCurrency)}–${this.money(hit.priceTo, hit.priceCurrency)}`
              : this.money(hit.priceFrom, hit.priceCurrency);

          // The CODE, because that is what identifies a product (ADR 0026) and
          // what get_product takes. Handing an agent a slug would hand it
          // something that changes when a shopkeeper fixes a typo.
          return `${hit.name} — ${price}${hit.inStock ? '' : ' (sold out)'} · code ${hit.code}`;
        });

        return say(`${page.total} result(s) for "${query}":\n${lines.join('\n')}`);
      },
    };
  }

  /**
   * The list on screen's facets (R-7.3, F-5). Phase 6 deferred filtering "until
   * the shop has facets to filter on"; this is that day. Both tools read and
   * change the page's own listing, so they refuse where there is none rather
   * than filter a list the shopper cannot see.
   */
  private listFilters(): ToolDescriptor {
    return {
      name: 'list_filters',
      description:
        'Lists what the product list on screen can be narrowed by — brand, price, ' +
        'colour, size, on offer… — with the code of each facet, the value of each ' +
        'option and how many products it would leave. Call it before apply_filter. ' +
        'Only works on a search or category page.',
      inputSchema: { type: 'object', properties: {} },
      execute: async () => {
        const listing = this.listing.current();
        const result = listing?.result();
        if (!listing || result?.status !== 'ready') return refuse(StorefrontTools.NoList);

        return say(describeFacets(result.facets, result.total));
      },
    };
  }

  /**
   * One facet's choices, applied to the list on screen the way a click applies
   * them: a navigation, so the shopper watches the list narrow, the URL holds it
   * and the back button undoes it. The agent narrows; it does not take the page
   * anywhere the shopper did not already go.
   */
  private applyFilter(): ToolDescriptor {
    return {
      name: 'apply_filter',
      description:
        "Narrows the product list on screen, in the shopper's view, by one facet " +
        "from list_filters. `values` REPLACES that facet's choices (several values " +
        'add up: two brands show both); an empty list clears it. For price send ' +
        'minPrice and/or maxPrice instead. Returns how many products are left.',
      inputSchema: {
        type: 'object',
        properties: {
          facet: { type: 'string', description: 'A facet code from list_filters, e.g. brand, price, COLOR.' },
          values: {
            type: 'array',
            items: { type: 'string' },
            description: 'Option values from list_filters, e.g. ["COLOR:BLACK"].',
          },
          minPrice: { type: 'number', description: 'For the price facet: the lowest price, inclusive.' },
          maxPrice: { type: 'number', description: 'For the price facet: the highest price, inclusive.' },
        },
        required: ['facet'],
      },
      execute: async (input) => {
        const listing = this.listing.current();
        const place = listing?.place();
        const result = listing?.result();
        if (!listing || !place || result?.status !== 'ready') return refuse(StorefrontTools.NoList);

        const choice: FilterChoice = {
          facet: String(input['facet'] ?? '').trim(),
          values: Array.isArray(input['values']) ? input['values'].map(String) : [],
          minPrice: input['minPrice'] === undefined ? null : Number(input['minPrice']),
          maxPrice: input['maxPrice'] === undefined ? null : Number(input['maxPrice']),
        };

        const outcome = chooseFilter(listing.filters(), result.facets, place, choice);
        if (!outcome.ok) return refuse(outcome.reason);

        try {
          const after = await listing.applyAndSettle(outcome.filters);
          if (after.status !== 'ready') return refuse('The shop could not list the products just now.');

          const lines = after.hits
            .slice(0, StorefrontTools.MaximumResults)
            .map((hit) => `${hit.name} · code ${hit.code}`);
          return say(`${after.total} product(s) now on screen:\n${lines.join('\n')}`);
        } catch {
          return refuse('The list did not answer in time.');
        }
      },
    };
  }

  private static readonly NoList =
    'There is no product list on screen. Filters narrow a search or category page the shopper is on.';

  private getProduct(): ToolDescriptor {
    return {
      name: 'get_product',
      description:
        'Returns one product in full: description, specifications, and every ' +
        'variant with its SKU, price and stock. Call it before add_to_cart, ' +
        'because add_to_cart takes a SKU and only this says which SKUs exist.',
      inputSchema: {
        type: 'object',
        properties: {
          code: { type: 'string', description: 'The product code from search_products.' },
        },
        required: ['code'],
      },
      execute: async (input) => {
        const code = String(input['code'] ?? '').trim();
        if (!code) return refuse('A product code is required.');

        try {
          const product = await firstValueFrom(this.products.get(code, this.culture.active()));

          const variants = product.variants
            .filter((variant) => !variant.discontinued)
            .map((variant) => {
              const stock = variant.inStock
                ? variant.remaining !== null && variant.remaining !== undefined
                  ? `${variant.remaining} left`
                  : 'in stock'
                : 'sold out';

              return (
                `  ${variant.sku} — ${variant.label || product.name} — ` +
                `${this.money(variant.priceAmount, variant.priceCurrency)} — ${stock}`
              );
            });

          const specs = product.attributes.map(
            (attribute) =>
              `  ${attribute.label}: ${attribute.value ?? attribute.number ?? attribute.flag}` +
              (attribute.unit ? ` ${attribute.unit}` : ''),
          );

          return say(
            [
              product.name,
              product.description ?? '',
              specs.length > 0 ? `Specifications:\n${specs.join('\n')}` : '',
              `Variants:\n${variants.join('\n')}`,
            ]
              .filter(Boolean)
              .join('\n\n'),
          );
        } catch {
          return refuse(`No product with code ${code}.`);
        }
      },
    };
  }

  private addToCart(): ToolDescriptor {
    return {
      name: 'add_to_cart',
      description:
        "Adds one variant to the shopper's cart by SKU. It does NOT choose the " +
        'variant: call get_product first and pick a SKU that is in stock. The ' +
        "cart is the shopper's own — the agent is acting in their session.",
      inputSchema: {
        type: 'object',
        properties: {
          sku: { type: 'string', description: 'The exact SKU from get_product.' },
          quantity: { type: 'number', description: 'How many. Defaults to 1.' },
        },
        required: ['sku'],
      },
      execute: async (input) => {
        const sku = String(input['sku'] ?? '').trim();
        if (!sku) return refuse('A SKU is required.');

        const quantity = Number(input['quantity'] ?? 1);
        if (!Number.isInteger(quantity) || quantity < 1) {
          return refuse('Quantity must be a whole number of at least 1.');
        }

        // The same call the button makes. If the server refuses — no stock, no
        // such SKU — the refusal comes back from the shop rather than from a
        // check this file invented, which is the only way the two paths cannot
        // disagree.
        await this.cart.add(sku, quantity);

        const failed = this.cart.failed();
        if (failed) return refuse(failed);

        return say(`Added ${quantity} × ${sku}. The cart now has ${this.cart.itemCount()} item(s).`);
      },
    };
  }

  private getCart(): ToolDescriptor {
    return {
      name: 'get_cart',
      description:
        "Returns what is in the shopper's cart, with the live total and any " +
        'discounts — INCLUDING the ones that did not apply and why, which is ' +
        'something the shop says out loud.',
      inputSchema: { type: 'object', properties: {} },
      execute: async () => {
        await this.cart.load();

        const cart = this.cart.cart();
        if (!cart || cart.lines.length === 0) return say('The cart is empty.');

        const lines = cart.lines.map((line) => `  ${line.quantity} × ${line.sku}`);
        const quote = this.cart.quote();

        // A suppressed discount and a discount of zero look identical in a
        // total, which is why the engine returns the reason (phase 3). An agent
        // that can explain why a promotion did NOT apply is more useful than one
        // that can only read the number.
        const discounts = (quote?.discounts ?? []).map((discount) =>
          discount.amount > 0
            ? `  ${discount.label}: −${this.money(discount.amount, quote?.currency ?? 'EUR')}`
            : `  ${discount.label}: not applied — ${discount.reason}`,
        );

        return say(
          [
            `Cart (${cart.itemCount} item(s)):\n${lines.join('\n')}`,
            discounts.length > 0 ? `Discounts:\n${discounts.join('\n')}` : '',
            quote ? `Total: ${this.money(quote.total, quote.currency)}` : '',
          ]
            .filter(Boolean)
            .join('\n\n'),
        );
      },
    };
  }

  /**
   * The shop's campaigns (R-8, H-5), from the same service the home's card and a
   * category's banner read — an agent sees what the shopper sees, in the order
   * the shop leads with.
   */
  private listCampaigns(): ToolDescriptor {
    return {
      name: 'list_campaigns',
      description:
        "Lists the shop's campaigns running now or starting within 30 days — Tendero Days, " +
        "a season, a category's week — each with its window, how long is left, what it " +
        'holds (with any coupon code) and a link to its products. Use it when the shopper ' +
        'asks whether there are deals or events.',
      inputSchema: { type: 'object', properties: {} },
      execute: async () => {
        const culture = this.culture.active();
        const list = await firstValueFrom(this.campaigns.list(culture));
        return say(describeCampaigns(list.items, culture, new Date()));
      },
    };
  }

  private money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  /**
   * Navigating is not a tool, on purpose.
   *
   * An agent that could move the page would be driving the shopper's browser
   * rather than helping in it, and this surface keeps the PERSON as the one who
   * navigates. The link a tool returns is the invitation; the click is theirs.
   *
   * `apply_filter` is the one change to the page an agent makes, and it keeps
   * the rule: it narrows the list the shopper already opened, where they watch
   * it happen and undo it with the back button. It was deferred in phase 6
   * until the shop had facets to filter on — a tool over a filter that did not
   * exist would have been the speculative configuration the article *The
   * feature flag you did not need* argues against — and it landed with them
   * (R-7.3, F-5).
   */
}
