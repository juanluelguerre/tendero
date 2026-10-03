import { computed, DestroyRef, Directive, inject, Injectable, signal, TemplateRef } from '@angular/core';

/**
 * The shell's one slot for the pane of a list's open item, beside the screen and
 * under the header, at Steward's left (the prototype's layout). A screen does not
 * place its pane: it declares it with `*backofficeSidePane` where it lives, and the
 * shell draws it in the slot — so every pane has the same frame, the same place and
 * its own scroll, and opening one never moves the list.
 */
@Injectable({ providedIn: 'root' })
export class SidePane {
  private readonly shown = signal<TemplateRef<unknown> | null>(null);

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
