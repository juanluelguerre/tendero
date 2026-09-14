using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// The value three things agree on (ADR 0031): the collection a vector goes
/// into, the model the startup check expects to find served, and the revision a
/// reindex targets. Every assertion here is about keeping two models — or two
/// revisions of one — from ever sharing a vector space.
/// </summary>
public sealed class EmbeddingModelDescriptorTests
{
    [Fact]
    public void Bge_m3_is_pinned_to_the_revision_whose_licence_was_checked()
    {
        var model = EmbeddingModelDescriptor.BgeM3;

        Assert.Equal("BAAI/bge-m3", model.ModelId);
        Assert.Equal("5617a9f61b028005a4858fdac845db406aefb181", model.Revision);
        Assert.Equal(1024, model.Dimensions);
    }

    [Fact]
    public void The_collection_is_named_after_the_model_the_revision_and_the_dimensions()
    {
        Assert.Equal("products_bge-m3_5617a9f_1024", EmbeddingModelDescriptor.BgeM3.CollectionName);
    }

    [Fact]
    public void Another_revision_of_the_same_model_writes_to_another_collection()
    {
        var bumped = new EmbeddingModelDescriptor("BAAI/bge-m3", "0123456789abcdef0123456789abcdef01234567", 1024);

        Assert.NotEqual(EmbeddingModelDescriptor.BgeM3.CollectionName, bumped.CollectionName);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("v1.0")]
    [InlineData("5617a9f")]
    [InlineData("5617A9F61B028005A4858FDAC845DB406AEFB181")]
    [InlineData("")]
    public void A_model_cannot_be_pinned_to_anything_but_a_full_commit(string revision)
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => new EmbeddingModelDescriptor("BAAI/bge-m3", revision, 1024));

        Assert.Equal("revision", refusal.ParamName);
    }

    [Fact]
    public void A_model_with_no_dimensions_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EmbeddingModelDescriptor("BAAI/bge-m3", EmbeddingModelDescriptor.BgeM3.Revision, 0));
    }

    // ---------- Registration: present only when there is a server ----------

    [Fact]
    public void Without_a_model_server_no_generator_is_registered_at_all()
    {
        var services = new ServiceCollection()
            .AddProductEmbeddingsIfConfigured(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        Assert.Null(services.GetService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.Null(services.GetService<EmbeddingModelDescriptor>());
    }

    [Fact]
    public void With_a_model_server_the_generator_and_its_descriptor_resolve_without_calling_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new(EmbeddingServiceCollectionExtensions.EndpointKey, "http://127.0.0.1:1")])
            .Build();

        var services = new ServiceCollection()
            .AddProductEmbeddingsIfConfigured(configuration)
            .BuildServiceProvider();

        Assert.NotNull(services.GetService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.Same(EmbeddingModelDescriptor.BgeM3, services.GetRequiredService<EmbeddingModelDescriptor>());
    }
}
