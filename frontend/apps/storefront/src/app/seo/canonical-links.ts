import { DOCUMENT, inject, Injectable } from '@angular/core';

/** One language's URL for the page being shown. */
export interface AlternateUrl {
  readonly culture: string;
  readonly path: string;
}

/**
 * The `<link>` tags that tell a search engine which URL is the real one and
 * where the other languages live.
 *
 * They are the half of `P5-13` that has no visible effect and is the reason the
 * locale went into the URL at all. While one route served two languages there
 * was nothing to declare: `hreflang` points at URLs, and there was only ever
 * one.
 *
 * **Three rules, and each of them is a way this is usually got wrong:**
 *
 * 1. A page lists ITSELF among its alternates. `hreflang` is a set of
 *    equivalents, not a list of "the others", and a page missing from its own
 *    set is ignored by Google rather than half-honoured.
 * 2. `x-default` names where somebody with no matching language should land.
 *    Without it, a Portuguese speaker gets whatever the crawler guesses.
 * 3. The canonical is ABSOLUTE. A relative canonical is legal and useless: it
 *    resolves against the page it is on, so it can never disagree with it,
 *    which is the one job it has.
 *
 * These tags are static markup, and until server rendering arrives a crawler
 * that does not execute JavaScript will not see them — which is a real
 * limitation and is recorded as one rather than pretended away. What they are
 * right now is correct, testable and ready for the moment `@angular/ssr` lands.
 */
@Injectable({ providedIn: 'root' })
export class CanonicalLinks {
  private readonly document = inject(DOCUMENT);

  /** Marks our own tags, so a re-render replaces them instead of piling up. */
  private static readonly Marker = 'data-tendero-seo';

  set(canonical: string, alternates: readonly AlternateUrl[], xDefault: string): void {
    this.clear();

    const origin = this.document.location.origin;
    const head = this.document.head;

    head.appendChild(this.link('canonical', `${origin}${canonical}`));

    for (const alternate of alternates) {
      head.appendChild(
        this.link('alternate', `${origin}${alternate.path}`, alternate.culture),
      );
    }

    head.appendChild(this.link('alternate', `${origin}${xDefault}`, 'x-default'));
  }

  /**
   * Called when a page cannot be canonicalised — a 404, a failure. Leaving the
   * previous product's tags in the head would tell a crawler that an error page
   * is the canonical URL of a product, which is worse than saying nothing.
   */
  clear(): void {
    this.document.head
      .querySelectorAll(`link[${CanonicalLinks.Marker}]`)
      .forEach((element) => element.remove());
  }

  private link(rel: string, href: string, hreflang?: string): HTMLLinkElement {
    const element = this.document.createElement('link');

    element.setAttribute(CanonicalLinks.Marker, '');
    element.setAttribute('rel', rel);
    element.setAttribute('href', href);

    if (hreflang) element.setAttribute('hreflang', hreflang);

    return element;
  }
}
