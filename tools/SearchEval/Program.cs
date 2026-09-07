using ElGuerre.Tendero.SearchEval;
using ElGuerre.Tendero.SearchEval.Suites;

// Tendero's quality gates. There is only one suite today; selection by --suite
// enters now and not with hybrid search, because if it arrived afterwards the
// hybrid one would be measured against a baseline that can no longer be compared
// with the committed one.

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

var suite = suites.FirstOrDefault(candidate => String.Equals(
    candidate.Name, options.Suite, StringComparison.OrdinalIgnoreCase));

if (suite is null)
{
    // Name the ones that exist, the way the import validator does with the
    // registered sources: an unknown name is a caller's error, and the answer is
    // the list, not a generic failure.
    Console.Error.WriteLine(
        $"Unknown suite '{options.Suite}'. Available: {String.Join(", ", suites.Select(s => s.Name))}.");
    return 2;
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
return await suite.RunAsync(options, cancellation.Token);
