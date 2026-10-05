import { computed, DestroyRef, Directive, inject, Injectable, signal, TemplateRef } from '@angular/core';
import { followsMedia } from '@tendero/shared-ui';

/** Where Steward is docked beside the page: it keeps a narrow table's 44 rem beside a 26 rem panel and the open rail. */
const StewardDockedFrom = '(min-width: 85rem)';

/** Where the pane of an item is docked too: there is room to spare for both. Below, it is a drawer over the page. */
const PaneDockedFrom = '(min-width: 112rem)';

/**
 * The shell's one slot for the pane of a list's open item. A screen does not place
 * its pane: it declares it with `*backofficeSidePane` where it lives, and the shell
 * draws it in the slot — so every pane has the same frame and its own scroll, and
 * opening one never moves the list. Docked beside the screen from 112 rem (under the
 * header, at Steward's left); below, a drawer over the screen, left of Steward when
 * Steward is docked: the two never share the page's width, so the list is never narrow.
 */
@Injectable({ providedIn: 'root' })
export class SidePane {
  private readonly shown = signal<TemplateRef<unknown> | null>(null);

  /** Steward sits beside the page; below, it is a drawer over it, and a sheet on a phone. */
  readonly stewardDocked = followsMedia(StewardDockedFrom);

  /** The item's pane sits beside the page only where there is room to spare; below, it is a drawer over it. */
  readonly docked = followsMedia(PaneDockedFrom);

  /** What the slot draws, if anything. */
  readonly template = this.shown.asReadonly();

  readonly isOpen = computed(() => this.shown() !== null);

  show(template: TemplateRef<unknown>): void {
    this.shown.set(template);
  }

  /** Only the pane that is shown can take itself away: a later one is not closed by an earlier one's end. */
  hide(template: TemplateRef<unknown>): void {
    if (this.shown() === template) this.shown.set(null);
  }
}

/**
 * <code>*backofficeSidePane</code> on a pane: it is in the slot while it exists, and
 * gone when it does not — an <code>@if</code> around it is how a screen opens and closes it.
 * The pane keeps the injectors of the screen that declared it, so the screen's own
 * store is the one it reads.
 */
@Directive({ selector: '[backofficeSidePane]' })
export class SidePaneContent {
  constructor() {
    const slot = inject(SidePane);
    const template = inject<TemplateRef<unknown>>(TemplateRef);
    slot.show(template);
    inject(DestroyRef).onDestroy(() => slot.hide(template));
  }
}
