import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ListState } from './list-state';

/** A list as a screen places it: its rows projected, an action for when there are none. */
@Component({
  imports: [ListState],
  template: `
    <backoffice-list-state
      [loading]="loading()"
      [failed]="failed()"
      [empty]="empty()"
      loadingText="Loading orders"
      failedText="The orders could not be loaded"
      emptyText="No orders yet"
    >
      <table>
        <tbody>
          <tr>
            <td>A-1</td>
          </tr>
        </tbody>
      </table>
      <a empty-action href="/catalogue">See the catalogue</a>
    </backoffice-list-state>
  `,
})
class AList {
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly empty = signal(false);
}

async function render() {
  const fixture = TestBed.createComponent(AList);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const status = () => element.querySelector('[role="status"]');
  return { fixture, element, status };
}

describe('the states of a list', () => {
  it('says it is loading in a region that stays, and shows the shape of rows', async () => {
    const { fixture, element, status } = await render();
    const region = status();
    const whileLoading = [
      region?.textContent?.trim(),
      !!element.querySelector('.skeleton-list'),
      !!element.querySelector('table'),
    ];

    fixture.componentInstance.loading.set(false);
    await fixture.whenStable();

    // The same element, emptied: a live region is announced when its words change, not when it arrives.
    expect([whileLoading, status() === region, region?.textContent?.trim()]).toEqual([
      ['Loading orders', true, false],
      true,
      '',
    ]);
  });

  it('shows the rows once loaded, and none of the states', async () => {
    const { fixture, element } = await render();
    fixture.componentInstance.loading.set(false);
    await fixture.whenStable();

    expect([
      element.querySelector('td')?.textContent,
      element.querySelector('.skeleton-list, [role="alert"], .empty'),
    ]).toEqual(['A-1', null]);
  });

  it('alerts a failure, in place of the rows', async () => {
    const { fixture, element } = await render();
    fixture.componentInstance.loading.set(false);
    fixture.componentInstance.failed.set(true);
    await fixture.whenStable();

    expect([element.querySelector('[role="alert"]')?.textContent?.trim(), element.querySelector('table')]).toEqual([
      'The orders could not be loaded',
      null,
    ]);
  });

  it('says there is nothing, with the screen’s way elsewhere', async () => {
    const { fixture, element } = await render();
    fixture.componentInstance.loading.set(false);
    fixture.componentInstance.empty.set(true);
    await fixture.whenStable();

    const empty = element.querySelector('.empty');
    expect([
      empty?.querySelector('.empty__text')?.textContent,
      empty?.querySelector('a')?.textContent,
      element.querySelector('table'),
    ]).toEqual(['No orders yet', 'See the catalogue', null]);
  });
});
