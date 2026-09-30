import { describe, expect, it } from 'vitest';
import { orderReference } from './order-reference';

/**
 * How an order is named to a person: eight characters they can read and type.
 * An order id is a version-7 Guid, whose START is a timestamp — every order
 * placed within a minute or so shares it — and whose END is random.
 */
describe('an order reference', () => {
  it('is the random end of the id, so two orders placed together read apart', () => {
    const first = '01a0f3c3-5c3e-7000-8000-00000000a1b2';
    const second = '01a0f3c3-5c3e-7000-8000-00000000c3d4';

    expect([orderReference(first), orderReference(second)]).toEqual(['0000a1b2', '0000c3d4']);
  });

  it('is what the orders search finds by its end', () => {
    const id = '0191d2a4-5c3e-7000-8000-9f8e7d6c5b4a';

    expect(id.endsWith(orderReference(id))).toBe(true);
  });
});
