import { describe, expect, it } from 'vitest';
import { shortId } from './short-id';

/** A long id shortened to what tells it apart: its random end, never its timestamp start. */
describe('a short id', () => {
  it('is the random end of a version-7 Guid, so two minted together read apart', () => {
    expect([shortId('01a0f3c3-5c3e-7000-8000-00000000a1b2'), shortId('01a0f3c3-5c3e-7000-8000-00000000c3d4')]).toEqual([
      '0000a1b2',
      '0000c3d4',
    ]);
  });

  it('is eight characters of a dashless trace id, not the whole of it', () => {
    expect(shortId('4bf92f3577b34da6a3ce929d0e0e4736')).toBe('0e0e4736');
  });

  it('leaves an id already short as it is', () => {
    expect(shortId('a1b2')).toBe('a1b2');
  });
});
