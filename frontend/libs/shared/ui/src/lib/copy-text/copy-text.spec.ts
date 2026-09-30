import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CopyText } from './copy-text';

@Component({
  imports: [CopyText],
  template: `
    <lib-copy-text value="http://localhost:4200/mcp" copyLabel="Copiar la dirección" copiedLabel="Copiada" />
    <lib-copy-text class="command" value="claude mcp add --transport http tendero http://localhost:4200/mcp" copyLabel="Copiar el comando" copiedLabel="Copiado" block />
  `,
})
class Host {}

/**
 * Something a person copies — an address, a command — shown and copyable in one
 * place (the author, 2026-09-30: the storefront's card had it and the agents page
 * did not). The clipboard is a permission a browser may refuse, so the text stays
 * on screen to select either way.
 */
describe('a text to copy', () => {
  afterEach(() => vi.unstubAllGlobals());

  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }

  const first = (element: HTMLElement) => element.querySelector('lib-copy-text') as HTMLElement;

  it('shows the text beside a button named for what it copies', () => {
    const element = render().nativeElement as HTMLElement;

    expect(first(element).querySelector('code')?.textContent?.trim()).toBe('http://localhost:4200/mcp');
    expect(first(element).querySelector('button')?.getAttribute('aria-label')).toBe('Copiar la dirección');
  });

  it('copies the text and says so, once', async () => {
    const writeText = vi.fn(async () => undefined);
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
    const fixture = render();
    const host = first(fixture.nativeElement);

    host.querySelector('button')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledWith('http://localhost:4200/mcp');
    expect(host.querySelector('[role="status"]')?.textContent?.trim()).toBe('Copiada');
  });

  it('says nothing when the browser refuses the clipboard, and the text is still there to select', async () => {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: async () => Promise.reject(new Error('denied')) } });
    const fixture = render();
    const host = first(fixture.nativeElement);

    host.querySelector('button')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(host.querySelector('[role="status"]')?.textContent?.trim()).toBe('');
    expect(host.querySelector('code')?.textContent?.trim()).toBe('http://localhost:4200/mcp');
  });

  it('keeps a long command as a block, its own lines kept', () => {
    const element = render().nativeElement as HTMLElement;

    expect(element.querySelector('lib-copy-text.command pre code')?.textContent).toContain('claude mcp add');
  });
});
