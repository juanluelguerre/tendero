using System.Runtime.CompilerServices;

// Connector adapters are internal (architecture rule 3: outside the assembly
// only the port is visible). Their contract suite lives in the tests, which do
// need to instantiate them.
[assembly: InternalsVisibleTo("ElGuerre.Tendero.Catalog.Tests")]
