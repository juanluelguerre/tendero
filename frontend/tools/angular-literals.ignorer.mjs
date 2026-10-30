// A StrykerJS ignorer: no mutant where Angular's compiler reads the code as a
// literal at build time.
//
// That is a decorator's argument (`@Component({ … })`, `@Injectable({ … })`)
// and the calls that declare a component's inputs, outputs and queries
// (`input()`, `input.required()`, `output()`, `model()`, `viewChild()`…). An
// instrumented one, every value wrapped in Stryker's switch, is refused outright
// (NG1010, "needs to be an object literal that is statically analyzable"), so a
// library with a component could not be run at all. What they say (a selector,
// an alias, `providedIn`) is configuration the build and the specs that render
// it already hold. The object is the plugin shape @stryker-mutator/api declares,
// written out so nothing imports a package only Stryker depends on.
const declarations = new Set([
  'input',
  'output',
  'model',
  'viewChild',
  'viewChildren',
  'contentChild',
  'contentChildren',
]);

const declares = (path) => {
  if (!path.isCallExpression()) return false;
  const callee = path.node.callee;
  const name = callee.type === 'MemberExpression' ? callee.object.name : callee.name;
  return declarations.has(name);
};

export const strykerPlugins = [
  {
    kind: 'Ignore',
    name: 'angular-literals',
    value: {
      shouldIgnore: (path) =>
        path.isDecorator() || declares(path) ? 'Angular reads it as a literal at build time' : undefined,
    },
  },
];
