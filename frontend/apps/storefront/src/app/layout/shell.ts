import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AgentTools } from '@tendero/shared-agent';
import { Culture, CultureStore } from '@tendero/shared-i18n';
import { StorefrontTools } from '../agent/storefront-tools';
import { CartStore } from '../data-access/cart.service';

/**
 * The storefront shell. It is NOT shared with the backoffice: one is roomy and
 * light and the other dense and dark, and unifying them would give a component
 * with a variant matrix. See docs/adr/0010.
 *
 * The header sticks. On a results page the search box is the thing people come
 * back to, and making them scroll up to reach it is the single most common way a
 * shop wastes a visit.
 */
@Component({
  selector: 'storefront-shell',
  imports: [RouterOutlet, RouterLink, TranslocoDirective],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  private readonly cultureStore = inject(CultureStore);

  protected readonly cultures = this.cultureStore.available;
  protected readonly culture = this.cultureStore.active;

  /**
   * Read once, here, so the badge is right on a cold load. Every page that
   * changes the cart updates the same store, so nothing else has to remember to
   * refresh it.
   */
  protected readonly cart = inject(CartStore);

  private readonly agent = inject(AgentTools);
  private readonly tools = inject(StorefrontTools);

  constructor() {
    void this.cart.load();

    // The shop opens itself to a browser agent, ONCE, from the one component
    // that exists for the whole session.
    //
    // This is the entire degradation story (`P6-5`, CLAUDE.md invariant 8):
    // without `navigator.modelContext` the call returns having done nothing,
    // nothing renders differently, and the shop is exactly the shop it was.
    // There is no flag, no fallback path and no second code path to keep
    // working — which is what "degrades gracefully" should mean and usually
    // does not.
    this.agent.register(this.tools.all());
  }

  private readonly router = inject(Router);

  /**
   * Switching language NAVIGATES; it does not just flip a signal.
   *
   * The URL is what carries the culture now (`P5-13`), so swapping the first
   * segment is the switch — and it means a language change is a page a person
   * can bookmark, share and go back from, rather than a hidden bit of state
   * that made two people looking at the same link see different words.
   *
   * The rest of the path is kept as it is. On a product page the SLUG is then
   * wrong for a moment — `/en/p/camisa-de-lino/K7M2QX9P4T` — and that is fine,
   * and is exactly why the code is the key: the page still resolves, and it
   * replaces the slug with the canonical one as soon as the answer arrives
   * (ADR 0026). Under a slug-keyed URL this switch would have been a 404.
   */
  protected use(culture: Culture): void {
    const [path, query] = this.router.url.split('?');
    const segments = path.split('/').filter(Boolean);

    segments[0] = culture;

    void this.router.navigateByUrl(`/${segments.join('/')}${query ? `?${query}` : ''}`);
  }

  /** Links have to carry the language segment, so they are built from it. */
  protected readonly home = computed(() => ['/', this.culture()]);
  protected readonly cartLink = computed(() => ['/', this.culture(), 'cart']);
  protected readonly accountLink = computed(() => ['/', this.culture(), 'account']);
}
