import { contrastMinimum, contrastOf } from './contrast';

/**
 * Held to pairs whose ratio is known: the extremes, WebAIM's reference pair, and
 * the design tokens' own documented ratios (`design/tokens.css`).
 */
describe('contrastOf', () => {
  it.each([
    ['#000000', '#FFFFFF', 21],
    ['#FFFFFF', '#FFFFFF', 1],
    ['#777777', '#FFFFFF', 4.48],
    ['rgb(228, 101, 93)', 'rgb(32, 29, 27)', 5.04],
    ['#DE8E16', '#201D1B', 6.37],
  ])('%s on %s is %s', (front, back, expected) => {
    expect(contrastOf(front, back)).toBeCloseTo(expected, 1);
  });

  it('composites a translucent foreground over its background', () => {
    expect(contrastOf('rgba(0, 0, 0, 0)', '#FFFFFF')).toBeCloseTo(1, 5);
    expect(contrastOf('rgba(0, 0, 0, 0.5)', '#FFFFFF')).toBeLessThan(contrastOf('#000000', '#FFFFFF'));
  });

  it('asks 3 of large text and 4.5 of the rest', () => {
    expect(contrastMinimum(24, 400)).toBe(3);
    expect(contrastMinimum(19, 700)).toBe(3);
    expect(contrastMinimum(19, 400)).toBe(4.5);
    expect(contrastMinimum(14, 700)).toBe(4.5);
  });
});
