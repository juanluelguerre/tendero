import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import type { Type } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { DashboardRefresh } from '../sources/dashboard-refresh';
import { DashboardSources } from '../sources/dashboard-sources';

/**
 * The widget specs' one way in (R-9): a widget with its real English words,
 * the dashboard's shared reads, and the answers its endpoints give — so a spec
 * says what the server answered and what the shopkeeper reads, and nothing else.
 */
export const English = JSON.parse(
  readFileSync(join(process.cwd(), 'apps', 'backoffice', 'public', 'i18n', 'en.json'), 'utf-8'),
);

export function setUpWidgets(): void {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideTenderoI18n(),
      provideRouter([]),
      { provide: API_BASE_URL, useValue: 'http://api' },
      DashboardSources,
      DashboardRefresh,
    ],
  });
  TestBed.inject(CultureStore).use('en');
}

/** Renders a widget and answers its requests: `answers` maps an API path (`/api/dashboard/sales`) to its body. */
export async function openWidget(
  component: Type<unknown>,
  answers: Record<string, object>,
  inputs: Record<string, unknown> = { period: 'Week' },
): Promise<HTMLElement> {
  return (await openWidgetFixture(component, answers, inputs)).nativeElement as HTMLElement;
}

/** The same, returning the fixture, for a spec that goes on after the first answers. */
export async function openWidgetFixture(
  component: Type<unknown>,
  answers: Record<string, object>,
  inputs: Record<string, unknown> = { period: 'Week' },
): Promise<ComponentFixture<unknown>> {
  const fixture = TestBed.createComponent(component);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  const http = TestBed.inject(HttpTestingController);

  // Two rounds: a request can be made only once the words or another answer arrive.
  for (let round = 0; round < 2; round++) {
    fixture.detectChanges();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush(English);
    for (const [path, body] of Object.entries(answers)) {
      for (const request of http.match((req) => req.url === `http://api${path}`)) request.flush(body);
    }
    await fixture.whenStable();
  }

  fixture.detectChanges();
  return fixture;
}
