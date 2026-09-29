/**
 * The HTTP status behind a resource's error. The response is either the error
 * itself or, when the runtime wraps a non-`Error` value, its `cause`.
 */
export function statusOf(error: unknown): number | undefined {
  const failure = error as { status?: number; cause?: { status?: number } } | undefined;
  return failure?.status ?? failure?.cause?.status;
}
