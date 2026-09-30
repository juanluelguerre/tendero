/**
 * An order as a person reads and types it: the last eight characters of its id.
 *
 * Not the first: an order id is a version-7 Guid, whose start is a timestamp, so
 * every order placed within a minute or so began the same — twenty-one orders
 * placed together all read `01a0f3c3`. The end is random and tells them apart,
 * and the orders search matches an id at either end, so what is shown is what
 * finds it.
 */
export function orderReference(id: string): string {
  return id.replace(/-/g, '').slice(-8);
}
