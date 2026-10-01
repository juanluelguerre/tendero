import type { ProductAttributeView, SpecSheet } from '@tendero/shared-api';
import { specLines, specStatements } from './spec-lines';

const attributes: ProductAttributeView[] = [
  { code: 'MATERIAL', label: 'Material', kind: 'Option', value: 'aluminio', number: null, unit: null, flag: null },
];

const sheet: SpecSheet = {
  products: [{ productId: 'p1', code: 'CAF-001', name: 'Moka', slug: 'moka' }],
  rows: [
    {
      attributeCode: 'MATERIAL',
      label: 'Material',
      cells: [
        {
          source: 'description',
          kind: 'Option',
          value: 'aluminio',
          number: null,
          unit: null,
          flag: null,
          evidence: { support: 0.93, quotes: [{ culture: 'es', text: 'de aluminio' }] },
        },
      ],
    },
    {
      attributeCode: 'CUPS',
      label: 'Tazas',
      cells: [{ source: 'none', kind: null, value: null, number: null, unit: null, flag: null, evidence: null }],
    },
  ],
  statements: [{ productId: 'p1', text: 'Tiene asa de baquelita.', evidence: { support: 0.9, quotes: [] } }],
};

describe('specLines', () => {
  it('takes the sheet when Knowledge answered, and leaves out what the product does not have', () => {
    const lines = specLines(attributes, sheet);

    expect(lines.map((line) => [line.code, line.cell.source])).toEqual([['MATERIAL', 'description']]);
    expect(specStatements(sheet).map((statement) => statement.text)).toEqual(['Tiene asa de baquelita.']);
  });

  /** Invariant 8: without the sheet the table is the catalogue's, and claims no source it does not know. */
  it('falls back to the catalogue attributes with no source when the sheet did not arrive', () => {
    const lines = specLines(attributes, undefined);

    expect(lines).toHaveLength(1);
    expect(lines[0].cell.source).toBe('');
    expect(lines[0].cell.value).toBe('aluminio');
    expect(specStatements(undefined)).toEqual([]);
  });
});
