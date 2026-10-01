import { ChangeDetectionStrategy, Component, computed, type ElementRef, input, output, signal, viewChild } from '@angular/core';

let dialogs = 0;

/**
 * The question an action that cannot be undone asks first (R-2, P12; R-1,
 * B-F5): a native modal dialog, so the platform traps the focus, closes on
 * `Esc` and makes the page behind it inert. It says what will follow in the
 * words of the screen that opens it, and can ask why — optionally, or as a
 * requirement where the domain keeps the reason.
 */
@Component({
  selector: 'backoffice-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog {
  readonly heading = input.required<string>();
  readonly body = input.required<string>();
  readonly confirmLabel = input.required<string>();
  readonly keepLabel = input.required<string>();
  /** Null asks no reason. */
  readonly reasonLabel = input<string | null>(null);
  readonly reasonRequired = input(false);
  readonly tone = input<'danger' | 'normal'>('danger');

  readonly confirmed = output<string | undefined>();

  protected readonly headingId = `confirm-${++dialogs}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly reason = signal('');
  protected readonly blocked = computed(() => this.reasonRequired() && !this.reason().trim());

  open(): void {
    this.reason.set('');
    this.dialog().nativeElement.showModal();
  }

  protected keep(): void {
    this.dialog().nativeElement.close();
  }

  protected confirm(): void {
    if (this.blocked()) return;
    this.dialog().nativeElement.close();
    this.confirmed.emit(this.reason().trim() || undefined);
  }
}
