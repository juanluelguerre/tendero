import { describe, expect, it } from 'vitest';
import { type Block, parseMarkdown } from './markdown';

const text = (value: string) => ({ kind: 'text', text: value });

/**
 * The subset of Markdown Steward's answers use (S-7's run, 2026-10-03): bold, italic,
 * code, headings, lists, tables. Parsed to data and drawn by the template, never to
 * HTML — what a model writes cannot become markup, whatever it writes.
 */
describe('parsing a steward answer', () => {
  it('reads bold, italic and code inside a line, and leaves what does not close as it was written', () => {
    const [paragraph] = parseMarkdown('Hay **22 productos** con *foto*, `SKU-1` y **sin cerrar') as Extract<
      Block,
      { kind: 'paragraph' | 'heading' }
    >[];

    expect(paragraph.inlines).toEqual([
      text('Hay '),
      { kind: 'strong', children: [text('22 productos')] },
      text(' con '),
      { kind: 'em', children: [text('foto')] },
      text(', '),
      { kind: 'code', text: 'SKU-1' },
      text(' y **sin cerrar'),
    ]);
  });

  it('does not take a lone asterisk or an underscore in a code for emphasis', () => {
    const [paragraph] = parseMarkdown('2 * 3 en BAGS_TOTE y B1_X_2') as Extract<
      Block,
      { kind: 'paragraph' | 'heading' }
    >[];

    expect(paragraph.inlines).toEqual([text('2 * 3 en BAGS_TOTE y B1_X_2')]);
  });

  it('reads ordered and bulleted lists, a line that starts with bold is not a bullet', () => {
    const blocks = parseMarkdown('1. Uno\n2. Dos\n\n* **Ventas:** 10\n* Otro\n\n**Resumen**');

    expect(blocks.map((block) => block.kind)).toEqual(['list', 'list', 'paragraph']);
    expect(blocks[0]).toMatchObject({ ordered: true, items: [{ depth: 0 }, { depth: 0 }] });
    expect(blocks[1]).toMatchObject({ ordered: false });
    expect((blocks[1] as Extract<Block, { kind: 'list' }>).items[0].inlines[0]).toEqual({
      kind: 'strong',
      children: [text('Ventas:')],
    });
  });

  it('reads a nested bullet by its indentation', () => {
    const [list] = parseMarkdown('- a\n  - b\n- c') as Extract<Block, { kind: 'list' }>[];

    expect(list.items.map((item) => item.depth)).toEqual([0, 1, 0]);
  });

  it('reads a table, with the numeric columns marked so the template can align them', () => {
    const [table] = parseMarkdown(
      '| Producto | Unidades | Ventas |\n|---|---:|---|\n| Bolsa | 9 | 1.234,50 € |\n| Mochila | 8 | 980 € |',
    ) as Extract<Block, { kind: 'table' }>[];

    expect(table.header.map((cell) => cell[0])).toEqual([text('Producto'), text('Unidades'), text('Ventas')]);
    expect(table.rows).toHaveLength(2);
    expect(table.numeric).toEqual([false, true, true]);
  });

  it('reads headings and fenced code, and keeps a code block as written', () => {
    const blocks = parseMarkdown('## Resumen\n```\n**no** *negrita*\n```');

    expect(blocks).toEqual([
      { kind: 'heading', inlines: [text('Resumen')] },
      { kind: 'code', text: '**no** *negrita*' },
    ]);
  });

  it('never makes markup of markup: HTML stays text', () => {
    const [paragraph] = parseMarkdown('<img src=x onerror=alert(1)> **<b>hi</b>**') as Extract<
      Block,
      { kind: 'paragraph' | 'heading' }
    >[];

    expect(JSON.stringify(paragraph.inlines)).toContain('<img src=x onerror=alert(1)>');
    expect(paragraph.inlines.every((node) => ['text', 'strong', 'em', 'code'].includes(node.kind))).toBe(true);
  });
});
