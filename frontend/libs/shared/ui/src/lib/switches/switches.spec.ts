import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import type { ThemeChoice } from '../theme';
import { CultureSwitch } from './culture-switch';
import { ThemeSwitch } from './theme-switch';

@Component({
  imports: [CultureSwitch, ThemeSwitch],
  template: `
    <lib-culture-switch
      label="Idioma"
      [labelVisible]="labelVisible()"
      [cultures]="cultures"
      [active]="culture()"
      (chosen)="culture.set($event)"
    />
    <lib-theme-switch label="Tema" [names]="names" [choice]="theme()" (chosen)="theme.set($event)" />
  `,
})
class Host {
  readonly cultures = ['es', 'en'] as const;
  readonly culture = signal<'es' | 'en'>('es');
  readonly labelVisible = signal(true);
  readonly names: Record<ThemeChoice, string> = { system: 'Del sistema', light: 'Claro', dark: 'Oscuro' };
  readonly theme = signal<ThemeChoice>('system');
}

/**
 * The account menu's two switches, one control for both apps (ADR 0049): each
 * choice a button that says whether it is pressed, in a group named by the label
 * beside it. What choosing does is the app's; a switch only says what was chosen.
 */
describe('the account switches', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const element = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;
  const group = (fixture: ReturnType<typeof render>, selector: string) =>
    element(fixture).querySelector(`${selector} [role="group"]`) as HTMLElement;
  const pressed = (fixture: ReturnType<typeof render>, selector: string) =>
    [...group(fixture, selector).querySelectorAll('button')].map((button) => button.getAttribute('aria-pressed'));

  it('presses the language in use, and says the one chosen', () => {
    const fixture = render();
    const before = pressed(fixture, 'lib-culture-switch');
    group(fixture, 'lib-culture-switch').querySelectorAll('button')[1].click();
    fixture.detectChanges();

    expect([before, pressed(fixture, 'lib-culture-switch')]).toEqual([
      ['true', 'false'],
      ['false', 'true'],
    ]);
  });

  it('names its group by the label beside it, or by the label alone where none is shown', () => {
    const fixture = render();
    const named = (fixture: ReturnType<typeof render>) => {
      const box = group(fixture, 'lib-culture-switch');
      const by = box.getAttribute('aria-labelledby');
      return by ? element(fixture).querySelector(`#${by}`)?.textContent?.trim() : box.getAttribute('aria-label');
    };
    const shown = named(fixture);
    fixture.componentInstance.labelVisible.set(false);
    fixture.detectChanges();

    expect([shown, named(fixture), element(fixture).querySelectorAll('lib-culture-switch span').length]).toEqual([
      'Idioma',
      'Idioma',
      0,
    ]);
  });

  it('gives each theme its name in the reader language, and says the one chosen', () => {
    const fixture = render();
    const buttons = () => [...group(fixture, 'lib-theme-switch').querySelectorAll('button')];
    const names = buttons().map((button) => button.getAttribute('aria-label'));
    buttons()[2].click();
    fixture.detectChanges();

    expect([names, pressed(fixture, 'lib-theme-switch'), fixture.componentInstance.theme()]).toEqual([
      ['Del sistema', 'Claro', 'Oscuro'],
      ['false', 'false', 'true'],
      'dark',
    ]);
  });
});
