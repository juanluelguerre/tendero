using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The worker's container can be built.
///
/// It sounds trivial and it is the test that was missing. The indexer came to
/// need the attribute definitions; the API registers them because it calls
/// <c>AddCatalog</c>, the worker did not, and the process stopped starting with
/// "Unable to resolve service for type IAttributeDefinitionReader". The build
/// was green, so were the 149 tests, and the integration ones did not see it
/// because they compose their own container with what they need.
///
/// It is the second time a composition failure went unnoticed — the first left
/// the API unable to start for a day and a half. The difference between them is
/// that a test written for something else found that one, and the user pressing
/// F5 found this one.
///
/// Neither Postgres nor Elasticsearch is needed: building the container opens no
/// connection, and what is being checked is the composition.
/// </summary>
public sealed class WorkerContainerTests
{
    [Fact]
    public void The_worker_container_builds()
    {
        using var provider = BuildWorkerServices();

        // ValidateOnBuild walks EVERY descriptor instead of waiting for somebody
        // to resolve the one that is missing, which is what turns this into a
        // check rather than a sample.
        Assert.NotNull(provider);
    }

    /// <summary>
    /// What the worker exists to do: project products into the index. If any of
    /// the three stops resolving, the outbox drains nowhere.
    /// </summary>
    [Theory]
    [InlineData(typeof(IProductIndexer))]
    [InlineData(typeof(IDomainEventHandler<ProductUpserted>))]
    [InlineData(typeof(IDomainEventHandler<ProductArchived>))]
    public void Everything_the_outbox_dispatches_to_can_be_resolved(Type service)
    {
        using var provider = BuildWorkerServices();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService(service));
    }

    private static ServiceProvider BuildWorkerServices()
    {
        var builder = Host.CreateApplicationBuilder();

        // The two strings the worker demands at startup. Neither is used: neither
        // the DbContext nor the Elastic client connects on construction.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:tendero-db"] = "Host=localhost;Database=worker-container-test",
            ["ConnectionStrings:elasticsearch"] = "http://localhost:9200"
        });

        builder.AddTenderoWorker();

        return builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
