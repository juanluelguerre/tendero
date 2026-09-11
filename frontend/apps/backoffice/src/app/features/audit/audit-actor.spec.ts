import { describe, expect, it } from 'vitest';
import { actorOf } from './audit-actor';

/**
 * Who a row says acted.
 *
 * A row with no subject used to print "system", and the UI review of
 * 2026-09-11 read a guest's checkout as something the system had done. The
 * system does sign its rows — the worker's principal carries the subject
 * "system" — so a row with no subject at all is somebody who never signed in:
 * a guest.
 */
describe('the actor of an audit row', () => {
  it('is the name when the row has one', () => {
    expect(actorOf({ actorName: 'Ana Ruiz', subject: 'ana' })).toEqual({ label: 'Ana Ruiz' });
  });

  it('is the subject when a row written before names existed has only that', () => {
    expect(actorOf({ actorName: null, subject: 'juanlu' })).toEqual({ label: 'juanlu' });
  });

  it('is the system when the worker signed it', () => {
    expect(actorOf({ actorName: 'system', subject: 'system' })).toEqual({ label: 'system' });
  });

  it('is a guest when nobody signed it at all', () => {
    expect(actorOf({ actorName: null, subject: null })).toEqual({ guest: true });
  });
});
