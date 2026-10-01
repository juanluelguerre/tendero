import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { ClaimsShown, WaitingStore } from './waiting.store';
import { WidgetFrame } from '../widget-frame/widget-frame';
import { valueOf } from '@tendero/shared-util';

/** What is waiting for the shopkeeper (P11-7): drafts to publish, claims to review, stock to restock — each leading to the screen that acts on it. */
@Component({
  selector: 'backoffice-waiting-widget',
  imports: [TranslocoDirective, RouterLink, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './waiting-widget.html',
  styleUrl: './waiting-widget.css',
  providers: [WaitingStore],
})
export class WaitingWidget {
  /** A resource read without throwing when its request failed (Angular 22). */
  protected readonly valueOf = valueOf;

  protected readonly store = inject(WaitingStore);
  private readonly culture = inject(CultureStore);

  protected readonly shown = ClaimsShown;

  protected statement(claim: { statementSpanish: string; statementEnglish: string }): string {
    return this.culture.active() === 'en' ? claim.statementEnglish : claim.statementSpanish;
  }
}
