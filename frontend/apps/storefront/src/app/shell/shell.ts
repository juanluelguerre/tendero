import { NgOptimizedImage, NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AgentTools } from '@tendero/shared-agent';
import { CultureStore } from '@tendero/shared-i18n';
import { followsMedia, Icon, ThemeStore } from '@tendero/shared-ui';
import { filter, map } from 'rxjs';
import { answerAgent, SearchFormTool } from '../agent/search-form-tool';
import { StorefrontTools } from '../agent/storefront-tools';
import { CartStore } from '../cart/cart.store';
import { departmentOf } from '../taxonomy/taxonomy-menu';
import { TaxonomyStore } from '../taxonomy/taxonomy.store';
import { ListingParam } from '../listing/listing-filters';
import { ShopLinks } from '../shop-links';
import { TinoDock } from '../tino/tino-dock/tino-dock';
import { TinoDoorStore } from '../tino/tino-door.store';
import { VisualSearchPanel } from '../visual-search/visual-search-panel/visual-search-panel';
import { Microphone } from '../voice/microphone/microphone';
import { VisualSearchStore } from '../visual-search/visual-search.store';
import { CompareTray } from '../compare/compare-tray/compare-tray';
import { ConsentBanner } from './consent-banner/consent-banner';
import { ShopMenu } from './shop-menu/shop-menu';
import { AccountMenu } from './account-menu/account-menu';

/** Where the menu stops being a panel and becomes the phone's drawer: the tablet breakpoint the backoffice uses. */

/**
 * The storefront shell. It is NOT shared with the backoffice: one is roomy and
 * light and the other dense (docs/adr/0010); what they share is the theme's
 * behaviour, from `shared-ui`.
 *
 * The redesign's R-5.1 gave it the way into the catalogue it never had (R-1,
 * S-F1): TIENDA is a button now, opening every category as a panel on a desktop
 * and a drawer on a phone, two interactions from any page. Only the header's
 * first row sticks.
 */
@Component({
  selector: 'storefront-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    TranslocoDirective,
    NgTemplateOutlet,
    ConsentBanner,
    CompareTray,
    ShopMenu,
    AccountMenu,
    TinoDock,
    VisualSearchPanel,
    Microphone,
    Icon,
    NgOptimizedImage,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
  // The host is the page's column, which its template cannot give itself.
  host: {
    class: 'flex flex-col min-h-dvh',
    // The room Tino's panel takes beside the page, while it is open there (shell.css).
    '[class.tino-docked]': 'tino.docked()',
    '(document:click)': 'clickedAt($event)',
    '(document:keydown.escape)': 'closeMenu(true)',
  },
})
export class Shell {
  private readonly cultureStore = inject(CultureStore);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly taxonomy = inject(TaxonomyStore);

  private readonly drawer = viewChild.required<ElementRef<HTMLDialogElement>>('drawer');
  /**
   * Not `required`: the docking effect below can run before the view is made —
   * a shell made while Tino's panel is already open read it then and threw
   * NG0951 (seen on 2026-10-14, when switching language still made a new shell).
   */
  private readonly page = viewChild<ElementRef<HTMLElement>>('page');
  private readonly menuButton = viewChild.required<ElementRef<HTMLButtonElement>>('menuButton');

  protected readonly culture = this.cultureStore.active;

  /**
   * Read once, here, so the badge is right on a cold load. Every page that
   * changes the cart updates the same store, so nothing else has to remember to
   * refresh it.
   */
  protected readonly cart = inject(CartStore);

  /** Tino's door: whether its panel is open, and whether Tino can be asked at all. */
  protected readonly tino = inject(TinoDoorStore);
  protected readonly visual = inject(VisualSearchStore);
  /** From the tablet turn the search box is the header row's; below it, the phone's own row. Which of the two is showing. */
  protected readonly wideEnoughForTheRow = followsMedia('(min-width: 48rem)');

  private readonly agent = inject(AgentTools);
  private readonly tools = inject(StorefrontTools);

  /** The desktop's panel. */
  /**
   * The department whose sections are open under the desktop's row, or none
   * (R-7.3, F-0). The row replaced R-5.1's single *Tienda* disclosure there: a
   * department is a link, one click from any page, and its sections are the
   * toggle beside it.
   */
  protected readonly openDepartment = signal<string | null>(null);
  protected readonly menuOpen = computed(() => this.openDepartment() !== null);
  protected readonly departments = computed(() => this.taxonomy.menu().map((department) => department.category));
  protected readonly links = inject(ShopLinks);
  protected readonly fromMenu = { via: 'menu' };
  /** The row's two shelves, in the listing's own parameters: `/es?offer=1` lists everything on offer. */
  protected readonly onOffer = { [ListingParam.offer]: '1' };
  protected readonly newArrivals = { [ListingParam.newArrivals]: '1' };
  /** The phone's drawer, open now; and whether it has ever been, so its menu is drawn only once it is wanted. */
  protected readonly drawerOpen = signal(false);
  protected readonly drawerOpened = signal(false);
  protected readonly expanded = computed(() => this.drawerOpen());

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** What the page is listing, for the header's box to show: the words of `?q=`, or nothing. */
  protected readonly query = computed(() => this.router.parseUrl(this.url()).queryParamMap.get('q') ?? '');

  /** The department of the category on screen, for the menu to mark (S-P2). */
  protected readonly department = computed(() => {
    const code = /\/c\/([^/?#]+)/.exec(this.url())?.[1];
    return code ? departmentOf(code, this.taxonomy.categories()) : null;
  });

  constructor() {
    void this.cart.load();
    this.agent.register(this.tools.all());

    // Created with the shell, so the page wears the chosen theme from the start.
    inject(ThemeStore);

    // Docked, the page under the header scrolls instead of the window (shell.css): where
    // the shopper was goes with them across the change, in both directions — the DOM's,
    // so an effect.
    const injector = inject(Injector);
    let docked = false;
    effect(() => {
      const now = this.tino.docked();
      if (now === docked) return;
      docked = now;
      const view = this.document.defaultView;
      // Before the view exists there is no scroll to carry across: the page
      // starts at its top either way.
      const page = this.page()?.nativeElement;
      if (!page) return;
      const at = now ? (view?.scrollY ?? 0) : page.scrollTop;
      afterNextRender(
        () => {
          if (now) page.scrollTop = at;
          else view?.scrollTo(0, at);
        },
        { injector },
      );
    });

    // A navigation is an answer: whatever was open to find it closes.
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.openDepartment.set(null));
  }

  /** The phone's and the tablet's way in: the drawer, with every department. */
  protected toggleMenu(): void {
    this.drawerOpened.set(true);
    this.drawerOpen.set(true);
    this.drawer().nativeElement.showModal();
  }

  /** A department's sections, under the desktop's row; its toggle again, or another's, closes them. */
  protected toggleDepartment(code: string): void {
    this.openDepartment.update((open) => (open === code ? null : code));
  }

  /** Esc, or a choice: the panel closes, and Esc gives the focus back to the button. */
  protected closeMenu(refocus: boolean): void {
    const open = this.openDepartment();
    if (open === null) return;
    this.openDepartment.set(null);
    if (refocus) this.document.querySelector<HTMLButtonElement>(`[data-department-toggle="${open}"]`)?.focus();
  }

  /** A click outside the header closes the panel, as a disclosure's should. */
  protected clickedAt(event: MouseEvent): void {
    if (!this.menuOpen() || this.drawer().nativeElement.open) return;
    const header = this.menuButton().nativeElement.closest('header');
    if (header && event.target instanceof Node && !header.contains(event.target)) this.openDepartment.set(null);
  }

  /**
   * The search form sent. A person's Enter searches; an agent's — the form is
   * also a tool, declared in the markup (`SearchFormTool`) — searches and is
   * told what the shopper is now looking at.
   */
  protected sendSearch(event: SubmitEvent, text: string): void {
    event.preventDefault();
    if (!answerAgent(event, text, (query) => this.find(query))) void this.find(text);
  }

  /**
   * What the phone's microphone heard (phase 14): written into the box, with the
   * focus at its end, and **not searched** — the shopper presses the magnifier,
   * as with words they typed. The box holds its own value: nothing binds it.
   */
  protected heardInto(box: HTMLInputElement, words: string): void {
    box.value = words;
    box.focus();
    box.setSelectionRange(words.length, words.length);
  }

  /** WebMCP's declarative proposal, on the header's form only: the phone's row is the same form twice. */
  protected readonly searchTool = SearchFormTool;

  protected find(text: string): Promise<boolean> {
    const q = text.trim();
    return this.router.navigate(['/', this.culture()], { queryParams: q ? { q } : {} });
  }

  /** Links have to carry the language segment, so they are built from it. */
  protected readonly home = computed(() => ['/', this.culture()]);
  protected readonly cartLink = computed(() => ['/', this.culture(), 'cart']);
}
