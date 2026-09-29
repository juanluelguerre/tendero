import { landing, stepOf } from './list-keys';

const press = (key: string, rest: Partial<KeyboardEvent> = {}) => ({
  key,
  ctrlKey: false,
  metaKey: false,
  altKey: false,
  target: document.body,
  ...rest,
});

describe('j and k down a queue', () => {
  it('reads j as down and k as up, and nothing else', () => {
    expect(stepOf(press('j'), false)).toBe(1);
    expect(stepOf(press('k'), false)).toBe(-1);
    expect(stepOf(press('x'), false)).toBeNull();
  });

  it('leaves the letter to somebody writing, holding a modifier, or in a dialog', () => {
    const field = document.createElement('input');
    expect(stepOf(press('j', { target: field }), false)).toBeNull();
    expect(stepOf(press('j', { ctrlKey: true }), false)).toBeNull();
    expect(stepOf(press('j'), true)).toBeNull();
  });

  it('starts from the right end and stops at the edges', () => {
    expect(landing(-1, 3, 1)).toBe(0);
    expect(landing(-1, 3, -1)).toBe(2);
    expect(landing(1, 3, 1)).toBe(2);
    expect(landing(2, 3, 1)).toBeNull();
    expect(landing(0, 3, -1)).toBeNull();
    expect(landing(0, 0, 1)).toBeNull();
  });
});
