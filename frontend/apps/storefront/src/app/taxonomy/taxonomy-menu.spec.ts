import type { CategoryView } from '@tendero/shared-api';
import { departmentOf, menuOf } from './taxonomy-menu';

const category = (code: string, parent: string | null, depth: number): CategoryView => ({ code, parent, name: code, path: [], depth });

const shop = [
  category('HOME', null, 0),
  category('BAGS', null, 0),
  category('KITCHEN', 'HOME', 1),
  category('TEXTILES', 'HOME', 1),
  category('COOKWARE', 'KITCHEN', 2),
  category('BACKPACK', 'BAGS', 1),
];

describe('the taxonomy as a menu', () => {
  it('draws departments, their sections, and a section\'s leaves', () => {
    const menu = menuOf(shop);

    expect(menu.map((department) => department.category.code)).toEqual(['HOME', 'BAGS']);
    expect(menu[0].groups.map((group) => [group.category.code, group.children.map((leaf) => leaf.code)])).toEqual([
      ['KITCHEN', ['COOKWARE']],
      ['TEXTILES', []],
    ]);
  });

  it('finds the department of any category, however deep', () => {
    expect(departmentOf('COOKWARE', shop)).toBe('HOME');
    expect(departmentOf('BACKPACK', shop)).toBe('BAGS');
    expect(departmentOf('HOME', shop)).toBe('HOME');
    expect(departmentOf('NOPE', shop)).toBeNull();
  });
});
