using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.Assist.Kits;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Assist.Tests.Kits;

/// <summary>
/// A sentence fills a kit's form (phase 13c, K-6). The model only chooses the
/// kit, among the shop's, as a closed question in plain text. Code reads the
/// budget and the requirements, which are written in the sentence (invariant
/// 12), and a kit is not built here: the solver still chooses.
/// </summary>
public sealed class UnderstandKitSentenceTests
{
    private static string Script(string name) => Path.Combine(AppContext.BaseDirectory, "scripts", name);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The shop's kits as Search's <c>list_kits</c> answers them, in the language asked.</summary>
    private sealed class Kits : IReadTool
    {
        public int Reads { get; private set; }

        public ToolAudience Audience => ToolAudience.Page;

        public string Name => "list_kits";

        public string Description => "The kits.";

        public JsonElement ArgumentsSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ReadAnswer> ReadAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            Reads++;
            var english = arguments.TryGetProperty("culture", out var culture) && culture.GetString() == "en";
            object kits = new
            {
                kits = new object[]
                {
                    new
                    {
                        code = "KITCHEN_STARTER",
                        name = english ? "Kitchen starter kit" : "Kit de cocina",
                        requirements = new[] { new { facet = "INDUCTION:true", label = english ? "Induction" : "Inducción" } },
                    },
                    new { code = "COFFEE_CORNER", name = english ? "Coffee corner" : "Rincón del café", requirements = Array.Empty<object>() },
                },
            };
            return Task.FromResult(new ReadAnswer(kits, 2, 2));
        }
    }

    private sealed class Shop(ServiceProvider provider, Kits kits) : IAsyncDisposable
    {
        public Kits Kits => kits;

        public async Task<KitSentenceUnderstood> Understand(string sentence, string culture = "es")
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .SendAsync(new UnderstandKitSentenceQuery(sentence, culture), Ct);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    /// <summary>The storefront's model is Tino's: one local model on the shopper's side.</summary>
    private static Shop Compose(string? model = "scripted", bool enabled = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tino:Enabled"] = enabled.ToString(),
            ["Tino:Model"] = model,
            ["Steward:Models:scripted:Kind"] = "Scripted",
            ["Steward:Models:scripted:Script"] = Script("kit-sentence.json"),
            // A model nobody answers for: the port is closed, the call fails at once.
            ["Steward:Models:broken:Endpoint"] = "http://127.0.0.1:9/v1/",
            ["Steward:Models:broken:Model"] = "broken",
            ["Steward:Models:broken:TimeoutSeconds"] = "2",
        }).Build();

        var kits = new Kits();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenderoCqrs(typeof(UnderstandKitSentenceQuery).Assembly);
        services.AddSingleton<IReadTool>(kits);
        services.AddAssist(configuration);
        services.AddModelProfiles(configuration);
        return new Shop(services.BuildServiceProvider(), kits);
    }

    private static Money Euros(decimal amount) => new(amount, ShopOptions.DefaultCurrency);

    [Fact]
    public async Task A_sentence_that_names_a_kit_a_budget_and_a_requirement_fills_all_three()
    {
        await using var shop = Compose();

        var understood = await shop.Understand("Quiero un kit de cocina para inducción por menos de 200 euros");

        Assert.Equal(new KitSentenceUnderstood("KITCHEN_STARTER", Euros(200m), ["INDUCTION:true"], true), understood, Same);
    }

    /// <summary>The same in English, read against the kits' English names and labels.</summary>
    [Fact]
    public async Task It_reads_a_sentence_in_the_language_asked()
    {
        await using var shop = Compose();

        var understood = await shop.Understand("A starter kitchen for induction under €59.90", "en");

        Assert.Equal(new KitSentenceUnderstood("KITCHEN_STARTER", Euros(59.90m), ["INDUCTION:true"], true), understood, Same);
    }

    /// <summary>A requirement the kit cannot hold a product to is not filled in: the coffee corner has none.</summary>
    [Fact]
    public async Task A_requirement_the_kit_does_not_offer_is_left_out()
    {
        await using var shop = Compose();

        var understood = await shop.Understand("algo para hacer café, apto para inducción, unos 150 €");

        Assert.Equal(new KitSentenceUnderstood("COFFEE_CORNER", Euros(150m), [], true), understood, Same);
    }

    /// <summary>The kit is enough to fill a form: a sentence with no amount leaves the budget for the shopper.</summary>
    [Fact]
    public async Task A_sentence_with_no_amount_names_the_kit_and_leaves_the_budget_empty()
    {
        await using var shop = Compose();

        var understood = await shop.Understand("algo para hacer café en casa");

        Assert.Equal(new KitSentenceUnderstood("COFFEE_CORNER", null, [], true), understood, Same);
    }

    [Theory]
    [InlineData("algo bonito para regalar por 30 euros")] // the model says none of these
    [InlineData("algo para el jardín por 80 €")] // the model names a kit the shop does not have
    public async Task A_sentence_that_names_none_of_the_shops_kits_is_not_understood(string sentence)
    {
        await using var shop = Compose();

        var understood = await shop.Understand(sentence);

        Assert.Equal(KitSentenceUnderstood.Not, understood, Same);
    }

    /// <summary>The form is there as it was: no model is not a refusal, and the page shows the form (invariant 8).</summary>
    [Theory]
    [InlineData("broken", true)] // the model does not answer
    [InlineData("scripted", false)] // the storefront's model is off
    [InlineData(null, true)] // no model is configured
    public async Task With_no_model_to_answer_nothing_is_understood_and_nothing_fails(string? model, bool enabled)
    {
        await using var shop = Compose(model, enabled);

        var understood = await shop.Understand("Quiero un kit de cocina para inducción por menos de 200 euros");

        Assert.Equal(KitSentenceUnderstood.Not, understood, Same);
        if (!enabled)
            Assert.Equal(0, shop.Kits.Reads);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_sentence_or_one_too_long_is_refused_before_anything_is_asked(string sentence)
    {
        await using var shop = Compose();

        await Assert.ThrowsAsync<ValidationException>(() => shop.Understand(sentence));
        await Assert.ThrowsAsync<ValidationException>(() => shop.Understand(new string('a', UnderstandKitSentenceQuery.MaxLength + 1)));
        Assert.Equal(0, shop.Kits.Reads);
    }

    /// <summary>Records with lists compare by reference; these are compared by what they say.</summary>
    private static readonly IEqualityComparer<KitSentenceUnderstood> Same = EqualityComparer<KitSentenceUnderstood>.Create(
        (a, b) => a is not null && b is not null
                  && (a.Template, a.Budget, a.Understood) == (b.Template, b.Budget, b.Understood)
                  && a.Requirements.SequenceEqual(b.Requirements));
}
