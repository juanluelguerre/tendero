import { HttpErrorResponse } from '@angular/common/http';
import { ApplicationRef, resource, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { readStatus } from './read-status';

/** What a screen says about a read (ADR 0047): the eleven screens that read a list said it the same way, each by hand. */
describe('the status of a read', () => {
  function read(answer: () => Promise<string>) {
    const ask = signal(0);
    const reading = TestBed.runInInjectionContext(() => resource({ params: () => ask(), loader: answer }));
    return { reading, status: readStatus(reading), again: () => ask.update((n) => n + 1) };
  }

  const settle = () => TestBed.inject(ApplicationRef).whenStable();

  it('is loading until the first answer, and not failed', async () => {
    const { status } = read(() => new Promise(() => undefined));
    TestBed.tick();

    expect([status.loading(), status.failed()]).toEqual([true, false]);
  });

  it('says it failed when the server does', async () => {
    const { status } = read(() => Promise.reject(new HttpErrorResponse({ status: 503 })));
    TestBed.tick();
    await settle();

    expect([status.loading(), status.failed()]).toEqual([false, true]);
  });

  /** An expired session is the door's to explain, not the screen's: the interceptor already sends the person there. */
  it('does not say it failed when the session expired', async () => {
    const { status } = read(() => Promise.reject(new HttpErrorResponse({ status: 401 })));
    TestBed.tick();
    await settle();

    expect(status.failed()).toBe(false);
  });
});
