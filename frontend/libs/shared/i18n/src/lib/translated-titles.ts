import { inject, Injectable, Provider } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, combineLatest, of, switchMap } from 'rxjs';

/** What names the current page: a translation key from its route, the page's
    own words when its name is data, or nothing at all. */
type PageName = { key: string } | { text: string } | null;

/**
 * The browser tab's title, per page and per language.
 *
 * Every page of the storefront used to say "Tendero Shop" and every page of
 * the backoffice "Tendero Backoffice" — the cart, the checkout, a product —
 * so a history list, a bookmark and a search result all read the same (UI
 * review, 2026-09-11).
 *
 * A route names itself with a translation KEY in its `title`, and this
 * translates it in the active language, again whenever the language changes,
 * followed by the app's `brand.title`. A page whose name is data — a product,
 * a category — calls {@link name} once it knows it. Shared because it is
 * behaviour both apps need identically (ADR 0010); the words stay per app.
 */
@Injectable({ providedIn: 'root' })
export class PageTitles extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private readonly page = new BehaviorSubject<PageName>(null);

  constructor() {
    super();

    this.page
      .pipe(
        switchMap((page) =>
          combineLatest([
            page === null ? of(null) : 'text' in page ? of(page.text) : this.transloco.selectTranslate(page.key),
            this.transloco.selectTranslate('brand.title'),
          ]),
        ),
      )
      .subscribe(([name, brand]) => this.title.setTitle(name ? `${name} · ${brand}` : brand));
  }

  /** The path the router is on, and the path a page last named itself on. */
  private path = '';
  private named: string | null = null;

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.path = pathOf(snapshot.url);

    // The same page under another query — a filter, a sort, a page number —
    // is the same page, and it will not name itself again: its name stays.
    if (this.named === this.path) return;

    this.named = null;
    const key = this.buildTitle(snapshot);
    this.page.next(key ? { key } : null);
  }

  /** For a page whose name is data — it replaces the route's until the
      router leaves that page; a change of query alone is not leaving it. */
  name(text: string): void {
    this.named = this.path;
    this.page.next({ text });
  }
}

/** An address without its query and fragment: what says which page it is. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0];
}

/** Route titles become translation keys, translated and branded. */
export function provideTranslatedTitles(): Provider[] {
  return [{ provide: TitleStrategy, useExisting: PageTitles }];
}
