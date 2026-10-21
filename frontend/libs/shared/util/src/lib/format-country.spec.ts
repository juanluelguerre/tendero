import { describe, expect, it } from 'vitest';
import { formatCountry } from './format-country';

describe('a country in the reader language', () => {
  it('names the code the way each language does', () => {
    expect(formatCountry('DE', 'es')).toBe('Alemania');
    expect(formatCountry('DE', 'en')).toBe('Germany');
  });

  it('takes the code in any case, as a typed address held it', () => {
    expect(formatCountry('pt', 'en')).toBe('Portugal');
  });

  /** Two letters the browser cannot name are still the address the shopper wrote. */
  it('says the code back when there is no name for it', () => {
    // AA is left to private use by the standard, so no language names it.
    expect(formatCountry('AA', 'es')).toBe('AA');
    expect(formatCountry('', 'es')).toBe('');
  });
});
