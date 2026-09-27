import type { ProductAttributeView, SpecCell, SpecSheet, SpecStatement } from '@tendero/shared-api';

/** One line of a product's specs table: a label, and a value with its source. */
export interface SpecLine {
  readonly code: string;
  readonly label: string;
  readonly cell: SpecCell;
}

/**
 * The product page's specs, from the spec sheet when Knowledge answered and from
 * the catalogue's own attributes when it did not (invariant 8).
 *
 * The fallback carries no source at all rather than "catalogue": without the
 * sheet the page does not know whether an approved claim put a value there, and
 * a chip that guessed would be the one thing this table exists not to do.
 */
export function specLines(attributes: readonly ProductAttributeView[], sheet: SpecSheet | undefined): SpecLine[] {
  if (sheet && sheet.products.length === 1)
    return sheet.rows
      .map((row) => ({ code: row.attributeCode, label: row.label, cell: row.cells[0] }))
      .filter((line) => line.cell && line.cell.source !== 'none');

  return attributes.map((attribute) => ({
    code: attribute.code,
    label: attribute.label,
    cell: {
      source: '',
      kind: attribute.kind,
      value: attribute.value ?? null,
      number: attribute.number ?? null,
      unit: attribute.unit ?? null,
      flag: attribute.flag ?? null,
      evidence: null,
    },
  }));
}

/** The approved sentences that are not a value — "it has an anti-theft pocket". */
export function specStatements(sheet: SpecSheet | undefined): readonly SpecStatement[] {
  return sheet?.statements ?? [];
}
