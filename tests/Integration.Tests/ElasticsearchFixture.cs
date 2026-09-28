using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// A throwaway Elasticsearch, one per test class that asks for it, at the image
/// and with the settings the AppHost runs (src/AppHost/AppHost.cs): single node,
/// security off, a 1 GB heap inside a 2 GB container.
///
/// Its own container and a random port, never the Elasticsearch on :9200 — on
/// the author's machine that one is somebody's work — and never the AppHost's on
/// :39200, which a running stack owns. With no Docker the tests skip, as the
/// Postgres ones do.
/// </summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    public const string Image = "docker.elastic.co/elasticsearch/elasticsearch:9.5.0";

    private IContainer? container;

    public ElasticsearchClient Client { get; private set; } = null!;

    /// <summary>The skip reason, or null when the node is up.</summary>
    public string? Unavailable { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!PostgresFixture.DockerIsListening())
        {
            Unavailable = "Docker is not available on this machine, so the integration tests cannot run.";
            return;
        }

        try
        {
            this.container = new ContainerBuilder(Image)
                .WithLabel(DockerDesktopGroup.ProjectLabel, DockerDesktopGroup.Project)
                .WithLabel(DockerDesktopGroup.ServiceLabel, "test-elasticsearch")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "false")
                .WithEnvironment("ES_JAVA_OPTS", "-Xms1g -Xmx1g")
                // The AppHost's limits, measured there: a 1 GB heap held 1.66 GB
                // of container memory under load.
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.HostConfig!.Memory = 2L * 1024 * 1024 * 1024;
                    parameters.HostConfig.MemorySwap = 2L * 1024 * 1024 * 1024;
                    parameters.HostConfig.NanoCPUs = 2_000_000_000;
                })
                .WithPortBinding(9200, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                    .ForPort(9200)
                    .ForPath("/_cluster/health")))
                .Build();

            await this.container.StartAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Unavailable = $"Could not start Elasticsearch: {exception.Message}";
            return;
        }

        Client = new ElasticsearchClient(new ElasticsearchClientSettings(
            new Uri($"http://{this.container.Hostname}:{this.container.GetMappedPublicPort(9200)}")));
    }

    public void SkipIfUnavailable()
    {
        if (Unavailable is not null)
            Assert.Skip(Unavailable);
    }

    public async ValueTask DisposeAsync()
    {
        if (this.container is not null)
            await this.container.DisposeAsync();
    }
}
