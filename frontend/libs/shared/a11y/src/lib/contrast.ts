/**
 * WCAG 2.2's contrast ratio (success criterion 1.4.3) between two CSS colours,
 * `rgb(…)` / `rgba(…)` / `#rrggbb`, the foreground composited over the background
 * when it is translucent. From 1 (none) to 21 (black on white).
 *
 * **Self-contained on purpose**: `auditPage` sends its source into the browser, so
 * it may not close over anything outside itself.
 */
export function contrastOf(foreground: string, background: string): number {
  const parse = (colour: string): [number, number, number, number] => {
    const text = colour.trim().toLowerCase();
    if (text.startsWith('#')) {
      const hex = text.slice(1);
      const full = hex.length === 3 ? [...hex].map((c) => c + c).join('') : hex;
      const alpha = full.length === 8 ? parseInt(full.slice(6, 8), 16) / 255 : 1;
      return [parseInt(full.slice(0, 2), 16), parseInt(full.slice(2, 4), 16), parseInt(full.slice(4, 6), 16), alpha];
    }
    const parts = text.replace(/^rgba?\(|\)$/g, '').split(/[\s,/]+/).filter(Boolean).map(Number);
    return [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0, parts[3] ?? 1];
  };
  const luminance = ([r, g, b]: [number, number, number, number]): number => {
    const [lr, lg, lb] = [r, g, b].map((v) => {
      const s = v / 255;
      return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
  };

  const back = parse(background);
  const front = parse(foreground);
  const a = front[3];
  const composited: [number, number, number, number] = [
    front[0] * a + back[0] * (1 - a),
    front[1] * a + back[1] * (1 - a),
    front[2] * a + back[2] * (1 - a),
    1,
  ];
  const [light, dark] = [luminance(composited), luminance(back)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

/** The minimum 1.4.3 asks: 3 for large text (24 px, or 18.66 px bold), 4.5 otherwise. */
export function contrastMinimum(fontSizePx: number, fontWeight: number): number {
  return fontSizePx >= 24 || (fontSizePx >= 18.66 && fontWeight >= 700) ? 3 : 4.5;
}
