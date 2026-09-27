import { compareToBaseline, findingsOf } from './ratchet';

/** The ratchet the end-to-end suites hold the interface to (R-1): nothing new, and fixes noticed. */
describe('the accessibility ratchet', () => {
  const audit = {
    overflow: 120,
    overflowing: ['a.tab "Returns"'],
    imagesWithoutAlt: [],
    unnamedControls: [],
    unlabelledFields: [],
    smallTargets: ['button.lang "es"', 'button.lang "es"'],
    lowContrast: ['span.muted "Madrid" 3.10:1 < 4.5'],
  };

  it('keys a finding by page, width, kind and element, never by its text or measured ratio', () => {
    expect(findingsOf('orders', 768, audit)).toEqual([
      'orders@768 lowContrast: span.muted < 4.5',
      'orders@768 overflowing: a.tab',
      'orders@768 smallTargets: button.lang',
    ]);
  });

  it('names what is new and what was fixed', () => {
    expect(compareToBaseline(['a', 'c'], ['a', 'b'])).toEqual({ added: ['c'], fixed: ['b'] });
  });
});
