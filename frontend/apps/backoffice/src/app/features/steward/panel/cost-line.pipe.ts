import { Pipe, type PipeTransform } from '@angular/core';
import type { ConversationCost } from '../steward.store';

/** The cost line's figures, each in the reader's language, for its sentence. */
export interface CostLine {
  readonly calls: string;
  readonly tokens: string;
  readonly euros: string;
  readonly seconds: string;
  readonly model: string;
}

/**
 * `{{ t('steward.cost', store.cost() | costLine: culture.active()) }}` — the
 * conversation's calls, tokens, euros and seconds in the reader's numbers. Euros
 * to four decimals: a turn on a cheap model costs a fraction of a cent, and €0.00
 * would say it was free.
 */
@Pipe({ name: 'costLine' })
export class CostLinePipe implements PipeTransform {
  transform(cost: ConversationCost, culture: string): CostLine {
    const whole = new Intl.NumberFormat(culture, { maximumFractionDigits: 0 });
    return {
      calls: whole.format(cost.calls),
      tokens: whole.format(cost.tokens),
      euros: new Intl.NumberFormat(culture, {
        style: 'currency',
        currency: 'EUR',
        minimumFractionDigits: 4,
        maximumFractionDigits: 4,
      }).format(cost.microEuros / 1_000_000),
      seconds: new Intl.NumberFormat(culture, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(
        cost.milliseconds / 1000,
      ),
      model: cost.model ?? '—',
    };
  }
}
