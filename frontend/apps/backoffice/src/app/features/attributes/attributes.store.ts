import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CatalogService } from '../../data-access/catalog.service';
import { readStatus } from '../../read-status';

/**
 * The attribute definitions' state (ADR 0047): the list, read once, and how
 * many have a label left untranslated — the question this screen exists for.
 */
export const AttributesStore = signalStore(
  withProps(() => {
    const catalog = inject(CatalogService);
    const definitions = rxResource({ stream: () => catalog.attributeDefinitions() });
    return { _definitions: definitions, ...readStatus(definitions) };
  }),
  withComputed((store) => {
    const items = computed(() => (store._definitions.hasValue() ? store._definitions.value().items : []));
    return {
      items,
      /** How many definitions have a label missing in some language. */
      incomplete: computed(() => items().filter((definition) => definition.missingCultures.length > 0).length),
    };
  }),
);
