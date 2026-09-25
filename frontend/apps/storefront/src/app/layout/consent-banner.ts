import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { AnalyticsConsent } from '@tendero/shared-telemetry';

/**
 * The analytics question, asked once (ADR 0036, `P9b-6`).
 *
 * A bar at the foot of the page and not a wall in front of it: the shop works
 * the same whatever the answer, so nothing waits for one. Yes and no are the
 * same size and the same weight — both quiet, so neither takes the page's one
 * clay action — because a refusal that takes more effort than an acceptance is
 * not a choice.
 */
@Component({
  selector: 'storefront-consent-banner',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './consent-banner.html',
  styleUrl: './consent-banner.css',
})
export class ConsentBanner {
  protected readonly consent = inject(AnalyticsConsent);
}
