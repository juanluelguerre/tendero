using System.Runtime.CompilerServices;

// The format adapters are internal (architecture rule 3: outside the assembly
// only the port is visible). Their tests, and the contract suite they inherit,
// need to instantiate them.
[assembly: InternalsVisibleTo("ElGuerre.Tendero.Exports.Tests")]
