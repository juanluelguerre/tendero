using System.Runtime.CompilerServices;

// Adapters are internal (architecture rule 3: outside the assembly only the port
// is visible). Their contract suites live in the tests, which do need to make them.
[assembly: InternalsVisibleTo("ElGuerre.Tendero.Accounts.Tests")]
