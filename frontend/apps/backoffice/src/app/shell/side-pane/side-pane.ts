import { computed, DestroyRef, Directive, inject, Injectable, signal, type Signal, TemplateRef } from '@angular/core';
import { StewardDoorStore } from '../../features/steward/steward-door.store';

/** Where a pane is docked beside the page: the page keeps a narrow table's 44 rem beside a 26 rem pane and the open rail. */
const DockedFrom = '(min-width: 85rem)';

/** Where Steward and the pane of an item are both docked, and so need not take turns. */
const BothDockedFrom = '(min-width: 112rem)';

/** Whether a media query matches, followed as the window changes. */
function follows(query: string): Signal<boolean> {
  const list = window.matchMedia(query);
  const matches = signal(list.matches);
  list.addEventListener('change', (event) => matches.set(event.matches));
  return matches.asReadonly();
}

/**
 * The shell's one slot for the pane of a list's open item, beside the screen and
 * under the header, at Steward's left (the prototype's layout). A screen does not
 * place its pane: it declares it with `*backofficeSidePane` where it lives, and the
 * shell draws it in the slot — so every pane has the same frame, the same place and
 * its own scroll, and opening one never moves the list.
 */
@Injectable({ providedIn: 'root' })
export class SidePane {
  private readonly door = inject(StewardDoorStore);
  private readonly shown = signal<TemplateRef<unknown> | null>(null);
  private readonly room = follows(BothDockedFrom);

  /** Panes sit beside the page; below, they are drawers over it (and sheets on a phone). */
  readonly docked = follows(DockedFrom);

  /** Docked, but with room for one only: Steward and an item's pane take turns. */
  readonly takesTurns = computed(() => this.docked() && !this.room());

  /** The item's pane is out of the way, not closed, while Steward has the room. */
  readonly yields = computed(() => this.takesTurns() && this.door.isOpen());

  /** What the slot draws, if anything. */
  readonly template = this.shown.asReadonly();

  readonly isOpen = computed(() => this.shown() !== null);

  show(template: TemplateRef<unknown>): void {
    this.shown.set(template);
    this.reveal();
  }

  /** A pane was opened or a new item picked in it: where the panes take turns, it has the room. */
  reveal(): void {
    if (this.takesTurns()) this.door.close();
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
