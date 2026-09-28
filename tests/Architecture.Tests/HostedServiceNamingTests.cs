using Microsoft.Extensions.Hosting;
using Xunit;

namespace ElGuerre.Tendero.Architecture.Tests;

/// <summary>
/// What runs beside a request says what it is and when it runs (the author,
/// 2026-09-28). Eight hosted services had grown five conventions:
/// `OutboxProcessor`, `StockSeeder`, `SearchIndexInitializer`,
/// `RebuildEmptySearchIndex`, `RecordEveryDescriptionAtStart`.
///
/// One rule now, which a name can be checked against:
/// - every hosted service ends in <c>Worker</c>, so a search for it finds them all;
/// - one that does its job once when the host starts — an <see cref="IHostedService"/>
///   whose <c>StartAsync</c> is the whole of it, not a <see cref="BackgroundService"/>
///   loop — says so before the suffix: <c>…AtStartupWorker</c>.
///
/// A <see cref="BackgroundService"/> may still be a one-shot that waits in the
/// background (the vector index's rebuild waits for its model server), so the
/// second half is enforced only where the type makes it certain.
/// </summary>
public sealed class HostedServiceNamingTests
{
    private static readonly Type[] HostedServices =
    [
        .. Solution.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IHostedService).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
    ];

    [Fact]
    public void Every_hosted_service_is_a_worker_by_name()
    {
        Assert.True(HostedServices.Length >= 8, $"Only {HostedServices.Length} hosted services were found; the scan is not seeing the solution.");

        var misnamed = HostedServices.Where(type => !type.Name.EndsWith("Worker", StringComparison.Ordinal)).Select(type => type.FullName);

        Assert.Empty(misnamed);
    }

    [Fact]
    public void A_hosted_service_that_only_starts_says_it_runs_at_startup()
    {
        var misnamed = HostedServices
            .Where(type => !typeof(BackgroundService).IsAssignableFrom(type))
            .Where(type => !type.Name.EndsWith("AtStartupWorker", StringComparison.Ordinal))
            .Select(type => type.FullName);

        Assert.Empty(misnamed);
    }
}
