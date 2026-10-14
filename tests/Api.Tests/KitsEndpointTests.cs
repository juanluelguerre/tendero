using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.Search;
using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.Search.Kits;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// GET /api/kits and POST /api/kits/{template}/build (phase 13c, K-2), through
/// the host: anyone may ask, a kit the shop does not offer is a 404 and never an
/// invention, a request that is not one is a 400, and what comes back names its
/// closed sets by name (ADR 0043) and its language in a header (ADR 0013).
/// </summary>
public sealed class KitsEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LocalizedText Text(string es, string en) => new(new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    private sealed class Templates : IKitTemplateReader
    {
        public Task<IReadOnlyList<KitTemplate>> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KitTemplate>>(
            [
                new KitTemplate("KITCHEN_STARTER", Text("Kit de cocina", "Kitchen starter kit"),
                [
                    new KitTemplateSlot("pan", Text("Sartén", "Frying pan"), "COOKWARE", Text("sartén", "frying pan"), Required: true),
                    new KitTemplateSlot("board", Text("Tabla de cortar", "Chopping board"), "COOKWARE", Text("tabla de cortar", "chopping board"), Required: false),
                ]),
            ]);
    }

    private sealed class Tree : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
        {
            var clock = TimeProvider.System;
            var cookware = Category.Define(clock, "COOKWARE", Text("Menaje", "Cookware"), attributes: ["INDUCTION"]);
            return Task.FromResult(new CategoryTree([cookware]));
        }
    }

    private sealed class Definitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttributeDefinitions(
                [AttributeDefinition.Define(TimeProvider.System, "INDUCTION", Text("inducción", "induction"), AttributeKind.Boolean, isFacet: true)]));
    }

    /// <summary>The shop's search: a pan at 49.90 and a board at 24.90.</summary>
    private sealed class Shop : ISearchStrategy
    {
        public Task<SearchResultPage> SearchAsync(ProductSearchQuery query, CancellationToken ct = default)
        {
            SearchHit[] hits = query.Text is "sartén" or "frying pan"
                ? [Hit("PAN", "Sartén de hierro", 49.90m)]
                : [Hit("BOARD", "Tabla de bambú", 24.90m)];
            return Task.FromResult(new SearchResultPage(hits, hits.Length, 1, query.PageSize, 0));
        }

        private static SearchHit Hit(string code, string name, decimal price) =>
            new($"id-{code}", code, name, code.ToLowerInvariant(), "Kuisina", "COOKWARE", $"v-{code}", $"SKU-{code}",
                new Money(price, "EUR"), new Money(price, "EUR"), new Money(price, "EUR"), null, InStock: true, Score: 1);
    }

    private static (HttpClient Client, IDisposable Owner) Compose()
    {
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IKitTemplateReader>(new Templates());
            services.AddSingleton<ICategoryReader>(new Tree());
            services.AddSingleton<IAttributeDefinitionReader>(new Definitions());
            services.AddKeyedScoped<ISearchStrategy>(SearchStrategies.Hybrid, (_, _) => new Shop());
        }));
        return (factory.CreateClient(), root);
    }

    [Fact]
    public async Task Nobody_signed_in_reads_the_kits_the_shop_offers_in_their_language()
    {
        var (client, owner) = Compose();
        using var _ = owner;

        var response = await client.GetAsync("/api/kits?culture=en", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
        var kit = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))[0];
        Assert.Equal(("KITCHEN_STARTER", "Kitchen starter kit"), (kit.GetProperty("code").GetString(), kit.GetProperty("name").GetString()));
        Assert.Equal(["Frying pan", "Chopping board"], kit.GetProperty("slots").EnumerateArray().Select(slot => slot.GetProperty("name").GetString()));
        Assert.Equal(("INDUCTION:true", "induction"),
            (kit.GetProperty("requirements")[0].GetProperty("facet").GetString(), kit.GetProperty("requirements")[0].GetProperty("label").GetString()));
    }

    /// <summary>The whole answer as a client reads it: amounts as numbers, closed sets by their names, the hit as the search gives it.</summary>
    [Fact]
    public async Task Nobody_signed_in_builds_a_kit_under_a_budget_and_reads_why_a_slot_is_empty()
    {
        var (client, owner) = Compose();
        using var _ = owner;

        var response = await client.PostAsJsonAsync("/api/kits/KITCHEN_STARTER/build", new { budget = new { amount = 6000, currency = "EUR" }, requirements = new[] { "INDUCTION:true" } }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("es", response.Content.Headers.ContentLanguage.Single());
        var kit = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        // Money as every surface says it: a whole number of minor units and its currency, each amount with its own.
        static (long, string?) Money(JsonElement money) => (money.GetProperty("amount").GetInt64(), money.GetProperty("currency").GetString());
        Assert.Equal("Kit de cocina", kit.GetProperty("name").GetString());
        Assert.Equal([(6000L, "EUR"), (4990L, "EUR"), (1010L, "EUR")],
            new[] { "budget", "total", "left" }.Select(member => Money(kit.GetProperty(member))));
        Assert.DoesNotContain(kit.EnumerateObject(), member => member.NameEquals("currency"));
        Assert.Equal(("Budget", true, true),
            (kit.GetProperty("binding").GetString(), kit.GetProperty("complete").GetBoolean(), kit.GetProperty("exact").GetBoolean()));

        // Why, a sentence a fact: each with the code that says what kind of thing
        // it says — the name, never a number — and the slot it is about, null for
        // the kit as a whole. A page composes its own sentence from the code; an
        // agent reads this one.
        Assert.Equal(
        [
            ("Summary", null, "Kit de cocina: 49,90 € de 60,00 €."),
            ("Chosen", "pan", "Sartén: Sartén de hierro, 49,90 €."),
            ("OverBudget", "board", "Sin tabla de cortar: lo más barato cuesta 24,90 € y quedaban 10,10 €."),
        ], kit.GetProperty("explanation").EnumerateArray().Select(said =>
            (said.GetProperty("code").GetString()!, said.GetProperty("about").GetString(), said.GetProperty("text").GetString()!)));

        var (pan, board) = (kit.GetProperty("slots")[0], kit.GetProperty("slots")[1]);
        Assert.Equal(("PAN", "SKU-PAN"), (pan.GetProperty("product").GetProperty("code").GetString(), pan.GetProperty("product").GetProperty("matchedSku").GetString()));
        Assert.Equal("INDUCTION:true", pan.GetProperty("requirements")[0].GetString());
        Assert.Equal(JsonValueKind.Null, board.GetProperty("product").ValueKind);
        Assert.Equal(("OverBudget", "BOARD", (1480L, "EUR")),
            (board.GetProperty("gap").GetProperty("reason").GetString(), board.GetProperty("gap").GetProperty("cheapest").GetProperty("code").GetString(),
                Money(board.GetProperty("gap").GetProperty("short"))));
    }

    /// <summary>No template, no kit: the shop does not make one up from an address.</summary>
    [Fact]
    public async Task A_kit_the_shop_does_not_offer_is_not_found()
    {
        var (client, owner) = Compose();
        using var _ = owner;

        var response = await client.PostAsJsonAsync("/api/kits/PICNIC/build", new { budget = new { amount = 6000, currency = "EUR" } }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{ "budget": { "amount": 0, "currency": "EUR" } }""")]
    [InlineData("""{ "budget": { "amount": 6000, "currency": "USD" } }""")]
    [InlineData("""{ "budget": { "amount": 6000, "currency": "EUR" }, "requirements": ["induction"] }""")]
    // The shape this phase left behind: a number with a fraction, and no currency.
    [InlineData("""{ "budget": 60 }""")]
    [InlineData("""{ "budget": { "amount": 60.5, "currency": "EUR" } }""")]
    public async Task A_request_that_is_not_one_is_refused_at_the_door(string body)
    {
        var (client, owner) = Compose();
        using var _ = owner;

        var response = await client.PostAsync("/api/kits/KITCHEN_STARTER/build", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
