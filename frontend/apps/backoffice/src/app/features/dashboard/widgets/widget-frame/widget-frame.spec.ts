import { Component, resource } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { WidgetFrame } from './widget-frame';

/** A widget as the dashboard's are written: its figure read from the resource, inside the frame's template. */
@Component({
  imports: [WidgetFrame],
  template: `
    <backoffice-widget-frame
      heading="Orders"
      link="/orders"
      linkText="All orders"
      [resource]="figures"
      loadingText="Loading the figures"
      failedText="The figures could not be loaded"
    >
      <span frame-aside class="aside">this week</span>
      <ng-template
        ><p class="figure">{{ figures.value() }}</p></ng-template
      >
    </backoffice-widget-frame>
  `,
})
class AWidget {
  /** The server's answer, given by the test when it chooses. */
  answer!: { resolve: (value: number) => void; reject: (error: Error) => void };
  readonly figures = resource({
    loader: () => new Promise<number>((resolve, reject) => (this.answer = { resolve, reject })),
  });
}

async function render() {
  const fixture = TestBed.createComponent(AWidget);
  fixture.detectChanges();
  TestBed.tick();
  const element = fixture.nativeElement as HTMLElement;
  return { fixture, element };
}

/** The answer arrives, and the page draws what it says. */
async function answered(fixture: ComponentFixture<AWidget>, give: (answer: AWidget['answer']) => void) {
  give(fixture.componentInstance.answer);
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('the frame a widget sits in', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('heads the widget with its title, the way to its list and what else the widget says there', async () => {
    const { element } = await render();

    expect([
      element.querySelector('h2')?.textContent?.trim(),
      element.querySelector('header a')?.getAttribute('href'),
      element.querySelector('header .aside')?.textContent,
    ]).toEqual(['Orders', '/orders', 'this week']);
  });

  it('says it is loading until the read first holds a value', async () => {
    const { element } = await render();

    expect([element.querySelector('[role="status"]')?.textContent?.trim(), element.querySelector('.figure')]).toEqual([
      'Loading the figures',
      null,
    ]);
  });

  it('shows the figures once they arrive', async () => {
    const { fixture, element } = await render();
    await answered(fixture, (answer) => answer.resolve(42));

    expect([element.querySelector('[role="status"]'), element.querySelector('.figure')?.textContent]).toEqual([
      null,
      '42',
    ]);
  });

  /** A failed resource throws when its value is read: the figures' template must not even be created. */
  it('says the read failed, and never reads what failed', async () => {
    const { fixture, element } = await render();
    await answered(fixture, (answer) => answer.reject(new Error('500')));

    expect([element.querySelector('[role="alert"]')?.textContent?.trim(), element.querySelector('.figure')]).toEqual([
      'The figures could not be loaded',
      null,
    ]);
  });
});
