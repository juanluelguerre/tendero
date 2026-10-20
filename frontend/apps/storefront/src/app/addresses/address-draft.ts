import { maxLength, pattern, required, schema } from '@angular/forms/signals';
import type { SavedAddress } from '@tendero/shared-api';

/**
 * An address as a form edits it: every field a string, since an input holds
 * text, the optional ones empty rather than null. The account's book and
 * checkout edit the same draft under the same schema, and the same names
 * (`address.line1`…), so a browser's autofill fills both (P15-2, P15-3).
 */
export interface AddressDraft {
  recipientName: string;
  line1: string;
  line2: string;
  city: string;
  region: string;
  postalCode: string;
  countryCode: string;
  phone: string;
}

/** A blank address in the shop's own country: the one field a form never leaves empty. */
export function emptyAddress(recipientName = ''): AddressDraft {
  return { recipientName, line1: '', line2: '', city: '', region: '', postalCode: '', countryCode: 'ES', phone: '' };
}

/** A kept address back into a draft: null becomes empty, because an input holds text. */
export function draftOf(address: SavedAddress): AddressDraft {
  return {
    recipientName: address.recipientName,
    line1: address.line1,
    line2: address.line2 ?? '',
    city: address.city,
    region: address.region ?? '',
    postalCode: address.postalCode,
    countryCode: address.countryCode,
    phone: address.phone ?? '',
  };
}

/** A draft as the API takes it: trimmed, the optional fields null when empty, the country in capitals. */
export function addressOf(draft: AddressDraft): SavedAddress {
  const optional = (value: string): string | null => value.trim() || null;
  return {
    recipientName: draft.recipientName.trim(),
    line1: draft.line1.trim(),
    line2: optional(draft.line2),
    city: draft.city.trim(),
    region: optional(draft.region),
    postalCode: draft.postalCode.trim(),
    countryCode: draft.countryCode.trim().toUpperCase(),
    phone: optional(draft.phone),
  };
}

/**
 * What a parcel needs before the server is asked, which is what the kernel's
 * `Address` refuses without: a name, a line, a city, a postcode, and a country as
 * two letters. The server still decides; this only keeps a form from asking it
 * a question it knows the answer to.
 */
export const addressSchema = schema<AddressDraft>((address) => {
  required(address.recipientName);
  // A name of spaces is no name: the server refuses it as one.
  pattern(address.recipientName, /\S/);
  required(address.line1);
  pattern(address.line1, /\S/);
  required(address.city);
  pattern(address.city, /\S/);
  required(address.postalCode);
  pattern(address.postalCode, /\S/);
  required(address.countryCode);
  maxLength(address.countryCode, 2);
  pattern(address.countryCode, /^[A-Za-z]{2}$/);
});
