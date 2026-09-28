import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DOCUMENT, type ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AgentTools } from '@tendero/shared-agent';
import { CultureStore } from '@tendero/shared-i18n';
import { ThemeStore } from '@tendero/shared-ui';
import { filter, map } from 'rxjs';
import { StorefrontTools } from '../agent/storefront-tools';
import { CartStore } from '../data-access/cart.service';
import { departmentOf, Taxonomy } from '../data-access/taxonomy';
import { CompareTray } from './compare-tray';
import { ConsentBanner } from './consent-banner';
import { ShopMenu } from './shop-menu';
import { AccountMenu } from './account-menu';

/** Where the menu stops being a panel and becomes the phone's drawer: the tablet breakpoint the backoffice uses. */
const DrawerBelow = '(max-width: 47.99rem)';

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
  imports: [RouterOutlet, RouterLink, TranslocoDirective, NgTemplateOutlet, ConsentBanner, CompareTray, ShopMenu, AccountMenu],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.css',
  host: { '(document:click)': 'clickedAt($event)', '(document:keydown.escape)': 'closeMenu(true)' },
})
export class Shell {
  private readonly cultureStore = inject(CultureStore);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly taxonomy = inject(Taxonomy);

  private readonly drawer = viewChild.required<ElementRef<HTMLDialogElement>>('drawer');
  private readonly menuButton = viewChild.required<ElementRef<HTMLButtonElement>>('menuButton');

  protected readonly culture = this.cultureStore.active;

  /**
   * Read once, here, so the badge is right on a cold load. Every page that
   * changes the cart updates the same store, so nothing else has to remember to
   * refresh it.
   */
  protected readonly cart = inject(CartStore);

  private readonly agent = inject(AgentTools);
  private readonly tools = inject(StorefrontTools);

  /** The desktop's panel. */
  protected readonly menuOpen = signal(false);
  /** The phone's drawer, open now; and whether it has ever been, so its menu is drawn only once it is wanted. */
  protected readonly drawerOpen = signal(false);
  protected readonly drawerOpened = signal(false);
  protected readonly expanded = computed(() => this.menuOpen() || this.drawerOpen());

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** The home page is a search: it does not get a second one in the header. */
  protected readonly onHome = computed(() => this.url().split(/[?#]/)[0].split('/').filter(Boolean).length <= 1);

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

    // A navigation is an answer: whatever was open to find it closes.
    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => this.menuOpen.set(false));
  }

  protected toggleMenu(): void {
    if (this.document.defaultView?.matchMedia(DrawerBelow).matches) {
      this.drawerOpened.set(true);
      this.drawerOpen.set(true);
      this.drawer().nativeElement.showModal();
      return;
    }
    this.menuOpen.update((open) => !open);
  }

  /** Esc, or a choice: the panel closes, and Esc gives the focus back to the button. */
  protected closeMenu(refocus: boolean): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    if (refocus) this.menuButton().nativeElement.focus();
  }

  /** A click outside the header closes the panel, as a disclosure's should. */
  protected clickedAt(event: MouseEvent): void {
    if (!this.menuOpen() || this.drawer().nativeElement.open) return;
    const header = this.menuButton().nativeElement.closest('header');
    if (header && event.target instanceof Node && !header.contains(event.target)) this.menuOpen.set(false);
  }

  protected find(text: string): void {
    const q = text.trim();
    void this.router.navigate(['/', this.culture()], { queryParams: q ? { q } : {} });
  }


  /** Links have to carry the language segment, so they are built from it. */
  protected readonly home = computed(() => ['/', this.culture()]);
  protected readonly cartLink = computed(() => ['/', this.culture(), 'cart']);
}
