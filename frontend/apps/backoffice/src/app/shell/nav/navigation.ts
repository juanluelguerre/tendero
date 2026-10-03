import type { IconName } from '@tendero/shared-ui';

/** A figure that says how much work waits on a screen, shown as its badge. */
export type WaitingKind = 'drafts' | 'claims' | 'toShip' | 'openReturns' | 'lowStock' | 'refusals' | 'proposals';

export interface NavItem {
  readonly route: string;
  /** Translation key under `nav.` */
  readonly label: string;
  readonly icon: IconName;
  readonly waiting?: WaitingKind;
}

export interface NavGroup {
  /** Translation key under `nav.groups.` */
  readonly label: string;
  readonly items: readonly NavItem[];
}

/**
 * The backoffice's fourteen screens as seven groups, in the order a working day
 * meets them (the redesign's R-3, P1). It replaced a row of twelve tabs that
 * pushed every page sideways on a tablet (R-1, B-F1). The rail and the phone's
 * drawer both draw from this list, so the two can never disagree.
 */
export const Navigation: readonly NavGroup[] = [
  { label: 'home', items: [{ route: '/dashboard', label: 'dashboard', icon: 'dashboard' }] },
  {
    label: 'catalogue',
    items: [
      { route: '/review', label: 'reviewQueue', icon: 'review', waiting: 'drafts' },
      { route: '/catalogue', label: 'catalogue', icon: 'products' },
      { route: '/claims', label: 'claims', icon: 'claims', waiting: 'claims' },
      { route: '/attributes', label: 'attributes', icon: 'attributes' },
    ],
  },
  {
    label: 'sales',
    items: [
      { route: '/orders', label: 'orders', icon: 'orders', waiting: 'toShip' },
      { route: '/returns', label: 'returns', icon: 'returns', waiting: 'openReturns' },
      { route: '/promotions', label: 'promotions', icon: 'promotions' },
      { route: '/campaigns', label: 'campaigns', icon: 'campaigns' },
    ],
  },
  { label: 'stock', items: [{ route: '/stock', label: 'stock', icon: 'stock', waiting: 'lowStock' }] },
  {
    label: 'agents',
    items: [
      { route: '/agents', label: 'agents', icon: 'agents' },
      { route: '/audit', label: 'audit', icon: 'audit', waiting: 'refusals' },
    ],
  },
  { label: 'insight', items: [{ route: '/search', label: 'search', icon: 'search' }] },
  {
    label: 'steward',
    items: [{ route: '/steward/proposals', label: 'proposals', icon: 'proposals', waiting: 'proposals' }],
  },
];

/** Where a url is, by the model's own labels: the group and the screen. */
export interface Crumbs {
  readonly group: string;
  readonly item: string;
  readonly route: string;
}

/**
 * The group and the screen a url belongs to — the longest route that is the url or
 * its parent — or null where there is no screen: the sign-in, an unknown address.
 * The header's breadcrumbs read it, so they come from the same list as the rail.
 */
export function crumbsOf(url: string): Crumbs | null {
  const path = url.split(/[?#]/)[0];
  let found: Crumbs | null = null;
  for (const group of Navigation)
    for (const item of group.items) {
      const here = path === item.route || path.startsWith(item.route + '/');
      if (here && (!found || item.route.length > found.route.length))
        found = { group: group.label, item: item.label, route: item.route };
    }
  return found;
}
