/**
 * j and k down a queue that is worked item by item (the redesign's R-3, P11):
 * the review queue and the claims. A step, or null when the key is not one —
 * and it is never one while somebody is writing, holding a modifier, or has a
 * dialog open, because then the letter belongs to them.
 */
export function stepOf(
  event: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'altKey' | 'target'>,
  dialogOpen: boolean,
): 1 | -1 | null {
  if (dialogOpen || event.ctrlKey || event.metaKey || event.altKey) return null;
  const target = event.target;
  if (
    target instanceof HTMLElement &&
    (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
  )
    return null;
  if (event.key === 'j') return 1;
  if (event.key === 'k') return -1;
  return null;
}

/** Where a step lands in a list: from nothing selected, j is the first and k the last; it stops at the ends. */
export function landing(index: number, count: number, step: 1 | -1): number | null {
  if (count === 0) return null;
  if (index < 0) return step === 1 ? 0 : count - 1;
  const next = index + step;
  return next < 0 || next >= count ? null : next;
}
