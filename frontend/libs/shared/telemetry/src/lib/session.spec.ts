import { sessionId } from './session';

function memoryStorage(): Pick<Storage, 'getItem' | 'setItem'> {
  const values = new Map<string, string>();
  return { getItem: (key) => values.get(key) ?? null, setItem: (key, value) => void values.set(key, value) };
}

describe('sessionId', () => {
  it('is the same id for every page of the tab', () => {
    const storage = memoryStorage();
    expect(sessionId(storage)).toBe(sessionId(storage));
  });

  it('is an opaque random id, never something about the person', () => {
    expect(sessionId(memoryStorage())).toMatch(/^[0-9a-f]{32}$/);
  });

  it('is a fresh id per page when storage is blocked, never an error', () => {
    const blocked = {
      getItem: () => {
        throw new Error('SecurityError');
      },
      setItem: () => undefined,
    };
    expect(sessionId(blocked)).not.toBe(sessionId(blocked));
    expect(sessionId(null)).toMatch(/^[0-9a-f]{32}$/);
  });
});
