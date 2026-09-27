import { fold, looksLikeId, matches, step } from './palette-search';

describe('the palette search', () => {
  it('folds case and accents away', () => {
    expect(fold('  Café Ñandú ')).toBe('cafe nandu');
  });

  it('finds a label when every word typed is in it, in any order', () => {
    expect(matches('Calidad de búsqueda', 'busqueda calidad')).toBe(true);
    expect(matches('Review queue', 'queue rev')).toBe(true);
    expect(matches('Review queue', 'returns')).toBe(false);
    expect(matches('Orders', '')).toBe(true);
  });

  it('takes hex of three characters or more for an id, and nothing else', () => {
    expect(looksLikeId('01a0e')).toBe(true);
    expect(looksLikeId('c84c3f')).toBe(true);
    expect(looksLikeId('01')).toBe(false);
    expect(looksLikeId('ana')).toBe(false);
  });

  it('goes round the options', () => {
    expect(step(2, 3, 1)).toBe(0);
    expect(step(0, 3, -1)).toBe(2);
    expect(step(-1, 3, 1)).toBe(0);
    expect(step(0, 0, 1)).toBe(-1);
  });
});
