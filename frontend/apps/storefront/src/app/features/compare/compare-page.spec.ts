import { codesFrom } from './compare-page';

describe('codesFrom', () => {
  it('reads a shared comparison URL the way a person types it', () => {
    expect(codesFrom(' caf-001, CAF-002 ,,caf-001 ')).toEqual(['CAF-001', 'CAF-002']);
  });

  it('keeps the four the server compares and nothing when there is nothing', () => {
    expect(codesFrom('A,B,C,D,E')).toEqual(['A', 'B', 'C', 'D']);
    expect(codesFrom(null)).toEqual([]);
  });
});
