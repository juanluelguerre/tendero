import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { SearchStatusStore } from './search-status.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/**
 * Search's status (spec, Operations; R-9, D-7): each stage said as what the
 * shopper gets — *Meaning: down since 11:40 — searches answer with words only* —
 * in a word as well as a colour. Not a period: it is now.
 */
@Component({
  selector: 'backoffice-search-status-widget',
  imports: [TranslocoDirective, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search-status-widget.html',
  styleUrl: './search-status-widget.css',
  providers: [SearchStatusStore],
})
export class SearchStatusWidget {
  protected readonly store = inject(SearchStatusStore);
  private readonly culture = inject(CultureStore);

  /** The time alone when the change was today, the short date and time otherwise: a status reads at a glance. */
  protected since(at: string): string {
    const when = new Date(at);
    const today = when.toDateString() === new Date().toDateString();
    return new Intl.DateTimeFormat(this.culture.active(), today ? { timeStyle: 'short' } : { dateStyle: 'short', timeStyle: 'short' }).format(when);
  }
}
