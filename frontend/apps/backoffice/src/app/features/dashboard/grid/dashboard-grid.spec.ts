import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DashboardGrid } from './dashboard-grid';
import { DASHBOARD_WIDGETS, type WidgetDefinition, type WidgetKey, type WidgetSize } from '../widgets/dashboard-widgets';

@Component({ selector: 'backoffice-stub-with-period', template: '<p>period {{ period() }}</p>' })
class WithPeriod {
  readonly period = input.required<string>();
}

@Component({ selector: 'backoffice-stub-without', template: '<p>no period</p>' })
class WithoutPeriod {}

function render(placements: { widget: WidgetKey; size: WidgetSize }[], period = 'Week') {
  const widgets = {
    salesByDay: { component: WithPeriod, takesPeriod: true },
    waiting: { component: WithoutPeriod, takesPeriod: false },
  } as unknown as Record<WidgetKey, WidgetDefinition>;
  TestBed.configureTestingModule({ providers: [{ provide: DASHBOARD_WIDGETS, useValue: widgets }] });
  const fixture = TestBed.createComponent(DashboardGrid);
  fixture.componentRef.setInput('placements', placements);
  fixture.componentRef.setInput('period', period);
  fixture.detectChanges();
  return fixture;
}

const cells = (element: HTMLElement) => [...element.querySelectorAll<HTMLElement>('.cell')];

describe('the dashboard grid', () => {
  it('lays the widgets out in their order, each at its size', () => {
    const element = render([{ widget: 'salesByDay', size: 'M' }, { widget: 'waiting', size: 'S' }]).nativeElement as HTMLElement;

    expect(cells(element).map((cell) => [cell.dataset['widget'], [...cell.classList].find((name) => name.startsWith('cell--'))])).toEqual([
      ['salesByDay', 'cell--M'],
      ['waiting', 'cell--S'],
    ]);
  });

  it('gives the period to the widgets that take one, and follows it when it changes', () => {
    const fixture = render([{ widget: 'salesByDay', size: 'M' }, { widget: 'waiting', size: 'S' }]);
    fixture.componentRef.setInput('period', 'Month');
    fixture.detectChanges();

    expect(cells(fixture.nativeElement).map((cell) => cell.textContent?.trim())).toEqual(['period Month', 'no period']);
  });

  it('marks itself to fill the screen when it is on a wall', () => {
    const fixture = render([{ widget: 'waiting', size: 'S' }]);
    fixture.componentRef.setInput('fill', true);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.grid')?.classList.contains('grid--fill')).toBe(true);
  });
});
