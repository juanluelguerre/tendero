using System.Runtime.CompilerServices;

// Los adaptadores de conector son internal (regla de arquitectura 3: fuera del
// ensamblado sólo se ve el puerto). Su suite de contrato vive en los tests, que
// sí necesitan instanciarlos.
[assembly: InternalsVisibleTo("ElGuerre.Tendero.Catalog.Tests")]
