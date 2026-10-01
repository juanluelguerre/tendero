import { describe, expect, it } from 'vitest';
import { valueOf } from './resource-value';

/** A resource read safely: its value when it has one, nothing while it loads or after it failed. */
describe('a resource value', () => {
  const resource = <T>(has: boolean, value: T) => ({
    hasValue: () => has,
    value: () => {
      if (!has) throw new Error('Resource is currently in an error state');
      return value;
    },
  });

  it('is the value once there is one', () => {
    expect(valueOf(resource(true, 42))).toBe(42);
  });

  it('is nothing while loading or after a failure, instead of throwing as Angular 22 does', () => {
    expect(valueOf(resource(false, 42))).toBeUndefined();
  });
});
