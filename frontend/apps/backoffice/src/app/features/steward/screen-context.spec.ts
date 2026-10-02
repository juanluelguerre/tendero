import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { screenOf } from './screen-context';

@Component({ template: '' })
class Blank {}

/**
 * The screen a question is asked on (spec, *The panel*): its name, and what it
 * shows — the open row, or else the filter — so "translate this one" needs no code.
 */
describe('the screen a question is asked on', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'catalogue', title: 'catalogue.title', component: Blank },
          { path: 'dashboard', component: Blank, children: [{ path: 'sales', component: Blank }] },
        ]),
      ],
    });
  });

  const name = (key: string) => `«${key}»`;

  async function at(url: string) {
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    return screenOf(router.routerState.snapshot.root, name);
  }

  it('names the screen by its title and the open row as its subject', async () => {
    expect(await at('/catalogue?status=Active&product=TND-0142')).toEqual({
      page: '«catalogue.title»',
      subject: 'TND-0142',
    });
  });

  it('takes the filter as the subject when no row is open', async () => {
    expect(await at('/catalogue?status=Active')).toEqual({ page: '«catalogue.title»', subject: 'Active' });
  });

  /** A screen with no title of its own is named by the rail's word for its first segment. */
  it('names a screen without a title by its place in the rail', async () => {
    expect(await at('/dashboard/sales')).toEqual({ page: '«nav.dashboard»', subject: null });
  });
});
