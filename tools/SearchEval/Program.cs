using ElGuerre.Tendero.SearchEval;
using ElGuerre.Tendero.SearchEval.Suites;

// Puertas de calidad de Tendero. Hoy sólo hay una suite; la selección por
// --suite entra ahora y no con la búsqueda híbrida, porque si llegara después
// la híbrida se mediría contra una línea base que ya no se puede comparar con
// la commiteada.

IEvaluationSuite[] suites = [new SearchRelevanceSuite()];

EvaluationOptions options;
try
{
    options = EvaluationOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(EvaluationOptions.Usage(suites));
    return 2;
}

var suite = suites.FirstOrDefault(
    candidate => string.Equals(candidate.Name, options.Suite, StringComparison.OrdinalIgnoreCase));

if (suite is null)
{
    // Nombrar las que hay, igual que hace el validador de importación con los
    // orígenes registrados: un nombre desconocido es un error del llamante y se
    // responde con la lista, no con un fallo genérico.
    Console.Error.WriteLine(
        $"Unknown suite '{options.Suite}'. Available: {string.Join(", ", suites.Select(s => s.Name))}.");
    return 2;
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
return await suite.RunAsync(options, cancellation.Token);
