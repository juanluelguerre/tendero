import { computed, type Resource, type Signal } from '@angular/core';
import { isSessionExpired } from '@tendero/shared-auth';

export interface ReadStatus {
  /** Only the first time: a list asked again keeps showing what it had. */
  readonly loading: Signal<boolean>;
  /** The server said no — unless the session expired, which is the door's to explain. */
  readonly failed: Signal<boolean>;
}

/**
 * What a screen says about a read (ADR 0047). Eleven screens read a list and
 * said it the same way, each by hand: loading until the first answer, failed
 * when the server fails, and nothing at all when the session has expired,
 * because the interceptor already sends the person to the door, which says why.
 */
export function readStatus(read: Resource<unknown>): ReadStatus {
  return {
    loading: computed(() => read.isLoading() && !read.hasValue()),
    failed: computed(() => {
      const failure = read.error();
      return failure !== undefined && !isSessionExpired(failure);
    }),
  };
}
