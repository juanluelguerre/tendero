import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import { describe, expect, it } from 'vitest';
import { addressOf, addressSchema, draftOf, emptyAddress, type AddressDraft } from './address-draft';

const madrid: AddressDraft = {
  recipientName: ' Ana García ',
  line1: 'Calle Mayor 12 ',
  line2: '',
  city: 'Madrid',
  region: '  ',
  postalCode: ' 28013',
  countryCode: 'es',
  phone: '',
};

/** The address one form edits for the account's book and for checkout (P15-2, P15-3). */
describe('an address draft', () => {
  it('goes to the API trimmed, its empty optional fields null, its country in capitals', () => {
    expect(addressOf(madrid)).toEqual({
      recipientName: 'Ana García',
      line1: 'Calle Mayor 12',
      line2: null,
      city: 'Madrid',
      region: null,
      postalCode: '28013',
      countryCode: 'ES',
      phone: null,
    });
  });

  it('comes back from a kept address with every null an empty field', () => {
    const kept = addressOf(madrid);

    expect(draftOf(kept)).toEqual({
      ...madrid,
      recipientName: 'Ana García',
      line1: 'Calle Mayor 12',
      region: '',
      postalCode: '28013',
      countryCode: 'ES',
    });
  });

  it('starts blank in the shop’s own country, with the signed-in name', () => {
    expect(emptyAddress('Ana García')).toMatchObject({ recipientName: 'Ana García', line1: '', countryCode: 'ES' });
  });

  /** What the kernel's Address refuses without, refused before the server is asked. */
  it.each<[string, Partial<AddressDraft>, boolean]>([
    ['complete', {}, true],
    ['a name of spaces', { recipientName: '   ' }, false],
    ['no street', { line1: '' }, false],
    ['a street of spaces', { line1: '  ' }, false],
    ['no city', { city: '' }, false],
    ['no postcode', { postalCode: '' }, false],
    ['a country of three letters', { countryCode: 'ESP' }, false],
    ['a country in digits', { countryCode: '34' }, false],
    ['no flat, region or phone', { line2: '', region: '', phone: '' }, true],
  ])('is %s → valid %s', (_, change, valid) => {
    const model = signal<AddressDraft>({ ...madrid, ...change });
    const tree = TestBed.runInInjectionContext(() => form(model, addressSchema));

    expect(tree().valid()).toBe(valid);
  });
});
