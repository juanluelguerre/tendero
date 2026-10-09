using System.Text.Json;
using ElGuerre.Tendero.Search.Kits;
using ElGuerre.Tendero.Search.Kits.Seed;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests.Kits;

/// <summary>
/// The shop's kits, read from the committed file (phase 13c) — and the file
/// itself held to the catalogue it is searched in, because a template that
/// names a category nobody has is a slot that is silently always empty.
/// </summary>
public sealed class SeedFileKitTemplatesTests
{
    private static string Seed(string file) => Path.Combine(RepositoryRoot(), "seed", file);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Tendero.slnx not found above the tests.");
    }

    private static Task<IReadOnlyList<KitTemplate>> Read(string path) =>
        new SeedFileKitTemplates(Options.Create(new KitSeedOptions { TemplatesPath = path })).AllAsync(TestContext.Current.CancellationToken);

    private static IReadOnlyList<KitTemplate> Written(string json) =>
        SeedFileKitTemplates.Read(
            JsonSerializer.Deserialize<SeedFileKitTemplates.Written[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!, "kits.json");

    private const string Slot = """{ "code": "pan", "name": { "es": "Sartén", "en": "Frying pan" }, "category": "cookware", "query": { "es": "sartén", "en": "frying pan" }, "required": true }""";

    [Fact]
    public async Task The_committed_kits_are_three_each_of_one_to_five_slots_in_both_languages()
    {
        var kits = await Read(Seed("kits.sample.json"));

        Assert.Equal(["KITCHEN_STARTER", "COFFEE_CORNER", "DESK"], kits.Select(kit => kit.Code));
        Assert.All(kits, kit =>
        {
            Assert.InRange(kit.Slots.Count, 1, KitTemplate.MaxSlots);
            Assert.Contains(kit.Slots, slot => slot.Required);
            Assert.All([kit.Name, .. kit.Slots.SelectMany(slot => (IEnumerable<SharedKernel.LocalizedText>)[slot.Name, slot.Query])], text =>
            {
                Assert.False(string.IsNullOrWhiteSpace(text.In("es")));
                Assert.False(string.IsNullOrWhiteSpace(text.In("en")));
                Assert.NotEqual(text.In("es"), text.In("en"));
            });
        });
    }

    /// <summary>Every slot looks in a category the catalogue has, and one that holds products: a leaf of the committed tree.</summary>
    [Fact]
    public async Task Every_slot_of_the_committed_kits_looks_in_a_category_the_catalogue_sells_in()
    {
        using var products = JsonDocument.Parse(await File.ReadAllTextAsync(Seed("products.sample.json"), TestContext.Current.CancellationToken));
        var sold = products.RootElement.EnumerateArray().Select(product => product.GetProperty("product_type").GetString()).ToHashSet();

        var kits = await Read(Seed("kits.sample.json"));

        Assert.All(kits.SelectMany(kit => kit.Slots), slot => Assert.Contains(slot.Category, sold));
    }

    /// <summary>No file is a shop that offers no kits — not a shop that is down.</summary>
    [Fact]
    public async Task With_no_file_the_shop_offers_no_kits()
    {
        Assert.Empty(await Read(Path.Combine(Path.GetTempPath(), $"no-kits-{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public void A_slots_category_is_read_as_the_catalogue_writes_its_codes()
    {
        var kit = Assert.Single(Written($$"""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [{{Slot}}] }]"""));

        Assert.Equal("COOKWARE", Assert.Single(kit.Slots).Category);
    }

    /// <summary>A file written wrong is refused whole and by name: a template that half loaded is a kit that silently lost its pan.</summary>
    [Theory]
    [InlineData("""[{ "code": "", "name": { "es": "Kit", "en": "A kit" }, "slots": [SLOT] }]""", "no code")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [] }]""", "0 slots")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" } }]""", "0 slots")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [SLOT, SLOT] }]""", "names a slot twice")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit" }, "slots": [SLOT] }]""", "both es and en")]
    [InlineData("""[{ "code": "K", "slots": [SLOT] }]""", "both es and en")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [SLOT] }, { "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [SLOT] }]""", "names a kit twice")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [{ "code": "pan", "name": { "es": "Sartén", "en": "Pan" }, "category": " ", "query": { "es": "sartén", "en": "pan" }, "required": true }] }]""", "no code or no category")]
    [InlineData("""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [{ "code": "pan", "name": { "es": "Sartén", "en": "Pan" }, "category": "COOKWARE", "query": { "es": "sartén", "en": " " }, "required": true }] }]""", "both es and en")]
    public void A_kit_written_wrong_is_refused_and_says_why(string json, string why)
    {
        var refused = Assert.Throws<InvalidDataException>(() => Written(json.Replace("SLOT", Slot, StringComparison.Ordinal)));

        Assert.Contains(why, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_kit_of_more_slots_than_the_solver_is_exact_at_is_refused()
    {
        var slots = string.Join(",", Enumerable.Range(0, KitTemplate.MaxSlots + 1).Select(index => Slot.Replace("\"pan\"", $"\"s{index}\"", StringComparison.Ordinal)));

        var refused = Assert.Throws<InvalidDataException>(() => Written($$"""[{ "code": "K", "name": { "es": "Kit", "en": "A kit" }, "slots": [{{slots}}] }]"""));

        Assert.Contains("6 slots", refused.Message, StringComparison.Ordinal);
    }
}
