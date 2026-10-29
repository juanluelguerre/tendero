using System.Runtime.CompilerServices;

// The adapters are internal (architecture rule 3: outside the assembly only the
// port is visible). Their tests, and the contract suites they inherit, need to
// instantiate them.
[assembly: InternalsVisibleTo("ElGuerre.Tendero.Shopify.Tests")]
