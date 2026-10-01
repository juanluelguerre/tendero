import { Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SpecEvidence, SpecSource as Source } from '@tendero/shared-api';
import { Telemetry } from '@tendero/shared-telemetry';

let next = 0;

/**
 * Where a spec came from, as a chip (P10-8). `catalogue` is a quiet label; a
 * value an approved claim put there opens the exact words it rests on and how
 * well they support it — the demo of ADR 0018: every row says its source, and
 * the sentence is one click away.
 *
 * The popover is the platform's (`popover` + `popovertarget`): keyboard,
 * Escape and light dismiss for free, and nothing to position by hand.
 */
@Component({
  selector: 'storefront-spec-source',
  imports: [TranslocoDirective],
  templateUrl: './spec-source.html',
  styleUrl: './spec-source.css',
})
export class SpecSource {
  readonly source = input.required<Source | string>();
  readonly evidence = input<SpecEvidence | null | undefined>(null);

  private readonly telemetry = inject(Telemetry);

  protected readonly id = `spec-source-${next++}`;

  /**
   * Support as a whole percentage — the NLI model's measure, not the extractor's
   * say-so — rounded DOWN: 0.996 read "100 %" in the first live run, which is a
   * certainty nothing here has.
   */
  protected readonly percent = computed(() => Math.floor((this.evidence()?.support ?? 0) * 100));

  /** Ties the popover to its chip where the browser supports anchor positioning; centred otherwise. */
  protected readonly anchor = `--${this.id}`;

  /** Only a value an approved claim put there has words to open, so the source is always `description` here. */
  protected opened(event: Event): void {
    if ((event as ToggleEvent).newState === 'open')
      this.telemetry.trackEvent('spec_source_opened', { source: 'description' });
  }
}
