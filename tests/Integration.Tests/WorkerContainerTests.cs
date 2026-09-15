using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Inventory.Domain;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.ServiceDefaults;
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
    /// <summary>
    /// Both shapes the worker starts in: without a vector layer, which is every
    /// fresh clone, and with one, which is <c>Tendero__Vectors=true</c>. The
    /// projection handlers resolve in both or the process does not start.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_worker_container_builds(bool withVectorLayer)
    {
        using var provider = BuildWorkerServices(withVectorLayer);

        // ValidateOnBuild walks EVERY descriptor instead of waiting for somebody
        // to resolve the one that is missing, which is what turns this into a
        // check rather than a sample.
        Assert.NotNull(provider);
    }

    /// <summary>
    /// What the worker exists to do. The outbox marks a message processed
    /// whether or not a handler was found for it, so a handler the worker cannot
    /// resolve — or an assembly it never scanned — is silent: orders would be
    /// placed, the messages would drain, and no stock would ever be held.
    ///
    /// This list is the closest thing the repository has to a declaration of
    /// what the outbox is FOR, which is why every arm of the saga is named here
    /// individually rather than checked as a group.
    /// </summary>
    [Theory]
    [InlineData(typeof(IProductIndexer))]
    [InlineData(typeof(IProductVectorIndexer))]
    [InlineData(typeof(IDomainEventHandler<ProductUpserted>))]
    [InlineData(typeof(IDomainEventHandler<ProductArchived>))]
    [InlineData(typeof(IDomainEventHandler<StockLevelChanged>))]
    [InlineData(typeof(IDomainEventHandler<OrderPlaced>))]
    [InlineData(typeof(IDomainEventHandler<OrderCancelled>))]
    [InlineData(typeof(IDomainEventHandler<OrderShipped>))]
    [InlineData(typeof(IDomainEventHandler<OrderPaymentAuthorized>))]
    public void Everything_the_outbox_dispatches_to_can_be_resolved(Type service)
    {
        using var provider = BuildWorkerServices();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService(service));
    }

    private static ServiceProvider BuildWorkerServices(bool withVectorLayer = false)
    {
        var builder = Host.CreateApplicationBuilder();

        // The two strings the worker demands at startup. Neither is used: neither
        // the DbContext nor the Elastic client connects on construction. Port 1,
        // where nothing listens — the defaults are somebody's real services
        // (see TenderoApiFactory).
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:tendero-db"] = "Host=127.0.0.1;Port=1;Database=worker-container-test;Timeout=1",
            ["ConnectionStrings:elasticsearch"] = "http://127.0.0.1:1"
        };

        // The two the AppHost adds for the vector layer, in the shapes it writes
        // them. Nothing connects on construction here either.
        if (withVectorLayer)
        {
            configuration["ConnectionStrings:qdrant"] = "Endpoint=http://127.0.0.1:1;Key=worker-container-test";
            configuration["services:embeddings:http:0"] = "http://127.0.0.1:1";
        }

        builder.Configuration.AddInMemoryCollection(configuration);

        // Exactly what Workers/Program.cs does, in its order. Building the worker
        // WITHOUT the service defaults checked a container no process runs: a
        // registration that lives there — the feature gate is one — would have
        // been missing here and present in production, and the test would have
        // failed for a reason that is not a defect.
        builder.AddServiceDefaults();
        builder.AddTenderoWorker();

        return builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
