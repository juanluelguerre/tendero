using System.Net.Http.Json;
using System.Numerics.Tensors;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The embedding generator against the real model server, at the pinned revision.
///
/// **Opt-in, and the reason is a measurement.** On a CPU this container downloads
/// 2.2 GB of weights and holds 12.7 GiB of RAM (ADR 0031) — more than a CI runner
/// has to spare, and more than anybody wants on every <c>dotnet test</c>. So it
/// runs only with <c>TENDERO_MODEL_TESTS=1</c>, and skips with a reason otherwise.
/// Everything that can be proven without the model — the request the SDK sends,
/// the descriptor, the registration — is proven in Search.Tests on every build.
///
/// It mounts the <c>tendero-tei-models</c> volume the AppHost uses, so a machine
/// that has run the vector layer once does not download the weights again.
/// </summary>
public sealed class EmbeddingModelServerTests
{
    private const string OptIn = "TENDERO_MODEL_TESTS";
    private const string Image = "ghcr.io/huggingface/text-embeddings-inference:cpu-1.9.3";

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Model")]
    public async Task The_served_model_is_the_pinned_revision_and_one_space_serves_both_cultures()
    {
        if (Environment.GetEnvironmentVariable(OptIn) != "1")
            Assert.Skip($"Needs the real model server: 2.2 GB of weights and ~13 GiB of RAM on a CPU. Set {OptIn}=1 to run it.");

        var model = EmbeddingModelDescriptor.BgeM3;
        var ct = TestContext.Current.CancellationToken;

        IContainer container;
        try
        {
            container = new ContainerBuilder(Image)
                .WithLabel(DockerDesktopGroup.ProjectLabel, DockerDesktopGroup.Project)
                .WithLabel(DockerDesktopGroup.ServiceLabel, "test-embeddings")
                .WithCommand("--model-id", model.ModelId, "--revision", model.Revision)
                .WithPortBinding(80, assignRandomHostPort: true)
                .WithVolumeMount("tendero-tei-models", "/data")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(80).ForPath("/health")))
                .Build();

            await container.StartAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Assert.Skip($"Could not start the model server: {exception.Message}");
            return;
        }

        await using (container)
        {
            var endpoint = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(80)}");

            // What the server says it loaded — the comparison P8-3 makes at startup.
            using var http = new HttpClient { BaseAddress = endpoint };
            var info = await http.GetFromJsonAsync<JsonElement>("/info", ct);
            Assert.Equal(model.ModelId, info.GetProperty("model_id").GetString());
            Assert.Equal(model.Revision, info.GetProperty("model_sha").GetString());

            var generator = new ServiceCollection()
                .AddProductEmbeddings(endpoint)
                .BuildServiceProvider()
                .GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            var vectors = await generator.GenerateAsync(
                ["cafetera italiana de 6 tazas", "Italian stovetop coffee maker, 6 cups", "mochila urbana de 20 litros"],
                cancellationToken: ct);

            Assert.All(vectors, vector => Assert.Equal(model.Dimensions, vector.Vector.Length));

            var spanishToEnglish = TensorPrimitives.CosineSimilarity(vectors[0].Vector.Span, vectors[1].Vector.Span);
            var coffeeMakerToBackpack = TensorPrimitives.CosineSimilarity(vectors[0].Vector.Span, vectors[2].Vector.Span);

            // The same product in two languages is closer than two products in one:
            // the property that lets one collection serve both cultures.
            Assert.True(spanishToEnglish > coffeeMakerToBackpack,
                $"es↔en {spanishToEnglish:F3} should exceed coffee maker↔backpack {coffeeMakerToBackpack:F3}");
        }
    }
}
