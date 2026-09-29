import type { CategoryView } from '@tendero/shared-api';

/** A second-level category and what hangs below it, if anything. */
export interface MenuGroup {
  readonly category: CategoryView;
  readonly children: readonly CategoryView[];
}

/** A department, the first level, with its groups in the catalogue's order. */
export interface MenuDepartment {
  readonly category: CategoryView;
  readonly groups: readonly MenuGroup[];
}

/**
 * The taxonomy as a menu: departments, their sections, and a section's leaves.
 * Pure, so the shape the menu draws is testable without a browser.
 */
export function menuOf(categories: readonly CategoryView[]): MenuDepartment[] {
  const childrenOf = (code: string) => categories.filter((category) => category.parent === code);
  return categories
    .filter((category) => category.depth === 0)
    .map((department) => ({
      category: department,
      groups: childrenOf(department.code).map((section) => ({ category: section, children: childrenOf(section.code) })),
    }));
}

/**
 * The department a category belongs to: the root of its path, or itself. The
 * menu marks it, as `aria-current` and not only in colour (R-2, S-P2).
 */
export function departmentOf(code: string, categories: readonly CategoryView[]): string | null {
  const byCode = new Map(categories.map((category) => [category.code, category]));
  let category = byCode.get(code);
  // Up by parent: `path` carries the ancestors' NAMES, for a breadcrumb, not their codes.
  for (let steps = 0; category && category.parent && steps < categories.length; steps++) category = byCode.get(category.parent);
  return category?.depth === 0 ? category.code : null;
}
