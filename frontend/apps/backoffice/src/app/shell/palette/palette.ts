import { ChangeDetectionStrategy, Component, type ElementRef, inject, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { BackofficeUsage } from '../../usage';
import type { PaletteOption } from './palette-search';
import { PaletteStore } from './palette.store';

/**
 * The command palette (the redesign's R-3, P2): Ctrl K, ⌘K or /, then a screen
 * by its name in either language, or an order, a product or an agent by what
 * the shopkeeper remembers of it. Enter goes there with its pane open.
 *
 * A native modal dialog holding the WAI-ARIA combobox pattern: the input keeps
 * the focus, the arrows move the active option, and the platform traps the
 * focus and closes on Esc. It only takes somebody somewhere; the action is
 * done in the pane, where its words and its confirmation are. What it finds
 * is its store's (ADR 0047); the dialog and the keys are its own.
 */
@Component({
  selector: 'backoffice-palette',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './palette.html',
  providers: [PaletteStore],
})
export class Palette {
  protected readonly store = inject(PaletteStore);
  private readonly router = inject(Router);
  private readonly usage = inject(BackofficeUsage);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  open(): void {
    if (this.store.isOpen()) return;
    this.store.opened();
    this.dialog().nativeElement.showModal();
    this.input().nativeElement.focus();
  }

  protected key(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.store.step(event.key === 'ArrowDown' ? 1 : -1);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const option = this.store.options()[this.store.active()];
      if (option) this.go(option);
    }
  }

  protected go(option: PaletteOption): void {
    this.usage.affordance('palette', option.kind);
    this.dialog().nativeElement.close();
    void this.router.navigate([option.route], { queryParams: option.queryParams });
  }
}
