import type { EnvironmentProviders, Provider } from '@angular/core';
import { provideResourceExtensions, withValueOnError } from '@ngrx/signals/resource';

/**
 * Every resource wrapped in `extendResource(...)` reads as no value once it
 * fails, instead of throwing (ADR 0051).
 *
 * In Angular 22 reading `value()` of a resource in its error state throws, and
 * so does every `computed` that reads it: a list whose request answered 500 sat
 * on "Loading" for good, its failure never reaching the template (found on
 * 2026-10-01). This was a hand-written `valueOf` at every read until NgRx's own
 * resource extensions were adopted; they are still marked experimental in
 * @ngrx/signals 22.0.1, the author's call with that on record. Both apps
 * provide it, and so do their test setups, so a store reads `list.value()`.
 */
export function provideReadableResources(): Provider | EnvironmentProviders {
  return provideResourceExtensions(withValueOnError(undefined));
}
