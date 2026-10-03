import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { describe, expect, it } from 'vitest';
import { Breadcrumbs } from './breadcrumbs';

@Component({ template: '' })
class Blank {}

const words = {
  nav: {
    breadcrumbs: 'Breadcrumb',
    catalogue: 'Products',
    stock: 'Stock',
    groups: { catalogue: 'Catalogue', stock: 'Stock' },
  },
};

/** The header's breadcrumbs: the group and the screen, once when they are the same word. */
describe('the breadcrumbs', () => {
  async function at(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        provideRouter([{ path: '**', component: Blank }]),
      ],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(Breadcrumbs);
    fixture.detectChanges();
    for (const request of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
      request.flush(words);
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the group and the screen, the screen as the current page', async () => {
    const element = await at('/catalogue?status=all');

    expect([...element.querySelectorAll('li:not([aria-hidden])')].map((item) => item.textContent?.trim())).toEqual([
      'Catalogue',
      'Products',
    ]);
    expect(element.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Products');
  });

  it('says a word once when the group and its only screen are the same', async () => {
    const element = await at('/stock');

    expect([...element.querySelectorAll('li:not([aria-hidden])')].map((item) => item.textContent?.trim())).toEqual([
      'Stock',
    ]);
  });

  it('shows nothing where there is no screen', async () => {
    const element = await at('/somewhere-else');

    expect(element.querySelector('nav')).toBeNull();
  });
});
