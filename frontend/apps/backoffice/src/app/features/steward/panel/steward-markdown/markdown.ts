/**
 * The subset of Markdown Steward's answers use — bold, italic, code, headings,
 * lists and tables — read into data (S-7's run, 2026-10-03). The template draws
 * the data, so what a model writes is text, never markup, whatever it writes.
 * Underscores are not emphasis: they are in the shop's codes (`BAGS_TOTE`).
 */
export type Inline =
  | { readonly kind: 'text' | 'code'; readonly text: string }
  | { readonly kind: 'strong' | 'em'; readonly children: readonly Inline[] };

export interface ListItem {
  readonly depth: number;
  readonly inlines: readonly Inline[];
}

export type Block =
  | { readonly kind: 'paragraph' | 'heading'; readonly inlines: readonly Inline[] }
  | { readonly kind: 'list'; readonly ordered: boolean; readonly items: readonly ListItem[] }
  | {
      readonly kind: 'table';
      readonly header: readonly (readonly Inline[])[];
      readonly rows: readonly (readonly (readonly Inline[])[])[];
      /** Columns of figures, which the template aligns to the end. */
      readonly numeric: readonly boolean[];
    }
  | { readonly kind: 'code'; readonly text: string };

const Token = /\*\*(.+?)\*\*|\*(?!\s)(.+?)(?<!\s)\*|`([^`\n]+)`/;
const Fence = /^\s*```/;
const Heading = /^\s{0,3}#{1,6}\s+(.*)$/;
const Item = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const Separator = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;
const Figure = /^[+\-−]?\s?[€$]?\s?\d[\d.,\s]*\s?(€|\$|%)?$/;

export function parseInline(source: string): Inline[] {
  const nodes: Inline[] = [];
  let rest = source;
  while (rest) {
    const found = Token.exec(rest);
    if (!found) {
      nodes.push({ kind: 'text', text: rest });
      break;
    }
    if (found.index > 0) nodes.push({ kind: 'text', text: rest.slice(0, found.index) });
    if (found[1] !== undefined) nodes.push({ kind: 'strong', children: parseInline(found[1]) });
    else if (found[2] !== undefined) nodes.push({ kind: 'em', children: parseInline(found[2]) });
    else nodes.push({ kind: 'code', text: found[3] });
    rest = rest.slice(found.index + found[0].length);
  }
  return nodes;
}

const cellsOf = (line: string): string[] =>
  line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());

const startsTable = (lines: readonly string[], at: number): boolean =>
  lines[at].includes('|') && at + 1 < lines.length && Separator.test(lines[at + 1]) && lines[at + 1].includes('-');

const startsBlock = (lines: readonly string[], at: number): boolean =>
  Fence.test(lines[at]) || Heading.test(lines[at]) || Item.test(lines[at]) || startsTable(lines, at);

export function parseMarkdown(source: string): Block[] {
  const lines = source.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let at = 0;

  while (at < lines.length) {
    const line = lines[at];
    if (!line.trim()) {
      at++;
      continue;
    }

    if (Fence.test(line)) {
      const code: string[] = [];
      for (at++; at < lines.length && !Fence.test(lines[at]); at++) code.push(lines[at]);
      at++;
      blocks.push({ kind: 'code', text: code.join('\n') });
      continue;
    }

    const heading = Heading.exec(line);
    if (heading) {
      blocks.push({ kind: 'heading', inlines: parseInline(heading[1].trim()) });
      at++;
      continue;
    }

    if (startsTable(lines, at)) {
      const header = cellsOf(line);
      const alignsEnd = cellsOf(lines[at + 1]).map((cell) => cell.endsWith(':'));
      const body: string[][] = [];
      for (at += 2; at < lines.length && lines[at].trim() && lines[at].includes('|'); at++) {
        const cells = cellsOf(lines[at]);
        body.push(header.map((_, column) => cells[column] ?? ''));
      }
      const numeric = header.map(
        (_, column) =>
          alignsEnd[column] ||
          (body.length > 0 && body.every((row) => row[column] === '' || Figure.test(row[column].replace(/\*/g, '')))),
      );
      blocks.push({
        kind: 'table',
        header: header.map(parseInline),
        rows: body.map((row) => row.map(parseInline)),
        numeric,
      });
      continue;
    }

    if (Item.test(line)) {
      const ordered = /^\s*\d/.test(line);
      const items: ListItem[] = [];
      for (; at < lines.length; at++) {
        const item = Item.exec(lines[at]);
        if (!item || (items.length > 0 && item[1].length < 2 && /^\d/.test(item[2]) !== ordered)) break;
        items.push({ depth: Math.min(2, Math.floor(item[1].length / 2)), inlines: parseInline(item[3].trim()) });
      }
      blocks.push({ kind: 'list', ordered, items });
      continue;
    }

    const paragraph: string[] = [];
    for (; at < lines.length && lines[at].trim() && (paragraph.length === 0 || !startsBlock(lines, at)); at++)
      paragraph.push(lines[at].trim());
    blocks.push({ kind: 'paragraph', inlines: parseInline(paragraph.join('\n')) });
  }

  return blocks;
}
