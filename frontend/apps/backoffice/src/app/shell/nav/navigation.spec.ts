import { describe, expect, it } from 'vitest';
import { crumbsOf, Navigation } from './navigation';

/** The breadcrumbs are read from the navigation model, so the rail and the header cannot disagree. */
describe('the breadcrumbs of a url', () => {
  it('names the group and the screen the url is on, whatever follows it', () => {
    expect(crumbsOf('/catalogue')).toEqual({ group: 'catalogue', item: 'catalogue', route: '/catalogue' });
    expect(crumbsOf('/orders?status=paid&page=2#top')).toMatchObject({ group: 'sales', item: 'orders' });
    expect(crumbsOf('/steward/proposals')).toMatchObject({ group: 'steward', item: 'proposals' });
    expect(crumbsOf('/claims/abc-123')).toMatchObject({ group: 'catalogue', item: 'claims' });
  });

  it('does not take a screen for another that only starts like it', () => {
    expect(crumbsOf('/reviewing')).toBeNull();
    expect(crumbsOf('/ordersx')).toBeNull();
  });

  it('has none where there is no screen: the sign-in and the unknown', () => {
    expect(crumbsOf('/sign-in')).toBeNull();
    expect(crumbsOf('/')).toBeNull();
  });

  it('finds every screen the rail shows', () => {
    for (const group of Navigation)
      for (const item of group.items)
        expect(crumbsOf(item.route), item.route).toMatchObject({ group: group.label, item: item.label });
  });
});
