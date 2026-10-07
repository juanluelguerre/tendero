using ElGuerre.Tendero.Catalog;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Integration.Tests;

/// <summary>
/// The attributes a claim may be typed with, as Catalog's definitions give them to
/// Knowledge (ADR 0014) — and which of them, by what the claim was read from. Over
/// the seed's own definitions, with no database: the adapter reads a file.
/// </summary>
public sealed class AttributeVocabularyTests
{
    private static async Task<IReadOnlyList<string>> OfferedFor(SourceKind readFrom)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Attributes:FilePath"] = Path.Combine(AppContext.BaseDirectory, "TestData", "attributes.sample.json"),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogReaders(configuration);
        // Never opened: the vocabulary is read from Catalog's definitions, which are a file.
        services.AddTenderoPersistence("Host=localhost;Database=never-opened");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var vocabulary = await scope.ServiceProvider.GetRequiredService<IAttributeVocabulary>()
            .CurrentAsync(readFrom, TestContext.Current.CancellationToken);
        return [.. vocabulary.Select(attribute => attribute.Code)];
    }

    /// <summary>A description may type what the catalogue defines, but not what tells variants apart, nor a size range.</summary>
    [Fact]
    public async Task A_description_is_offered_the_catalogues_attributes_but_its_variant_axes()
    {
        var offered = await OfferedFor(SourceKind.ProductDescription);

        Assert.Contains("CAPACITY_L", offered);
        Assert.Contains("COLOUR_TEMPERATURE", offered);
        Assert.Contains("MATERIAL", offered);
        Assert.Contains("PIECES", offered);
        Assert.DoesNotContain("COLOR", offered);
        Assert.DoesNotContain("SIZE_RANGE", offered);
    }

    /// <summary>
    /// A picture shows how many there are, and nothing else the catalogue has a
    /// name for today (the author's ruling, 2026-10-07): no power, no capacity,
    /// no colour temperature — and not a material, which a flat illustration does
    /// not tell. The colour it does show is a variant axis, offered to nobody.
    /// </summary>
    [Fact]
    public async Task A_picture_is_offered_only_what_a_picture_can_show()
    {
        Assert.Equal(["PIECES"], await OfferedFor(SourceKind.ProductImage));
    }
}
