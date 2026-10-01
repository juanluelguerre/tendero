import { ApplicationRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { signalStore } from '@ngrx/signals';
import { describe, expect, it } from 'vitest';
import { withWorklistUrl, WorklistPageSize } from './with-worklist-url';

const ListStore = signalStore(withWorklistUrl());

@Component({ template: '', providers: [ListStore] })
class ListPage {}

/** What every list of work keeps in its URL (ADR 0047): the page, beside whatever filter the list has. */
describe('a list of work in the URL', () => {
  async function at(url: string) {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'orders', component: ListPage }]), ListStore],
    });
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    return { store: TestBed.inject(ListStore), router };
  }

  async function settled() {
    await TestBed.inject(ApplicationRef).whenStable();
  }

  it('reads the page from the URL, and the first page from anything that is not one', async () => {
    const pages = [];
    for (const url of ['/orders?page=3', '/orders', '/orders?page=-2', '/orders?page=x']) {
      const { store } = await at(url);
      pages.push(store.page());
      TestBed.resetTestingModule();
    }

    expect(pages).toEqual([3, 1, 1, 1]);
  });

  it('goes to a page keeping the filter, and says nothing for the first', async () => {
    const { store, router } = await at('/orders?status=Shipped');
    store.go(2);
    await settled();
    const second = router.url;
    store.go(1);
    await settled();

    expect([second, router.url]).toEqual(['/orders?status=Shipped&page=2', '/orders?status=Shipped']);
  });

  it('pages by the same size everywhere', async () => {
    const { store } = await at('/orders');

    expect([store.pageSize, WorklistPageSize]).toEqual([20, 20]);
  });
});
