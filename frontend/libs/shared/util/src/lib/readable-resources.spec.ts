// @vitest-environment jsdom
import '@angular/compiler';
import { ApplicationRef, resource } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { setupTestBed } from '@analogjs/vitest-angular/setup-testbed';
import { extendResource } from '@ngrx/signals/resource';
import { describe, expect, it } from 'vitest';
import { provideReadableResources } from './readable-resources';

setupTestBed();

/**
 * A resource that failed reads as "no value", so a list whose request answered
 * 500 shows its failure instead of sitting on "Loading" for good (2026-10-01).
 */
describe('readable resources', () => {
  async function failed(providers: unknown[]) {
    TestBed.configureTestingModule({ providers: providers as never[] });
    const read = TestBed.runInInjectionContext(() =>
      extendResource(
        resource({
          loader: async () => {
            throw new Error('500');
          },
        }),
      ),
    );
    await TestBed.inject(ApplicationRef).whenStable();
    return read;
  }

  it('read as no value once they fail, with the reason in their status', async () => {
    const read = await failed([provideReadableResources()]);

    expect([read.status(), read.value()]).toEqual(['error', undefined]);
  });

  it('are needed: without them Angular 22 throws on the value of a failed resource', async () => {
    const read = await failed([]);

    expect(read.status()).toBe('error');
    expect(() => read.value()).toThrow();
  });
});
