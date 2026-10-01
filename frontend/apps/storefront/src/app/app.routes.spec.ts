import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CultureStore } from '@tendero/shared-i18n';
import { appRoutes } from './app.routes';

/**
 * Where an address lands, on the real routes — the redirects and the guards run,
 * nothing is rendered.
 *
 * An unknown path inside a language once had nowhere to go: it fell through to
 * the redirect for paths with no language, which added the language again —
 * `/es/x`, `/es/es/x`, `/es/es/es/x` — and the tab froze with nothing in the
 * console.
 */
describe('the storefront routes', () => {
  async function land(url: string) {
    // The preferred language is Spanish. The redirect asks for it once per turn,
    // so a loop asks again and again without ever yielding — which would freeze
    // this test rather than fail it. The sixth question fails it instead.
    let asked = 0;
    const active = () => {
      if (++asked > 5) throw new Error(`the redirect looped: ${url} asked for a language ${asked} times`);
      return 'es';
    };

    TestBed.configureTestingModule({
      providers: [provideRouter(appRoutes), { provide: CultureStore, useValue: { active, use: () => undefined } }],
    });
    const router = TestBed.inject(Router);

    await router.navigateByUrl(url);

    let leaf = router.routerState.snapshot.root;
    while (leaf.firstChild) leaf = leaf.firstChild;

    return { url: router.url, path: leaf.routeConfig?.path };
  }

  it('shows an unknown page inside a language as not found, at the address asked for', async () => {
    expect(await land('/es/no-such-page')).toEqual({ url: '/es/no-such-page', path: '**' });
  });

  it('gives an unknown page with no language the preferred one, then shows it as not found', async () => {
    expect(await land('/no-such-page')).toEqual({ url: '/es/no-such-page', path: '**' });
  });

  it('still gives a known page with no language the preferred one', async () => {
    expect(await land('/cart?from=mail')).toEqual({ url: '/es/cart?from=mail', path: 'cart' });
  });
});
