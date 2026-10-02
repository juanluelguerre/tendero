using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetProductTranslation;
using ElGuerre.Tendero.Catalog.Features.WithdrawProduct;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests.Features;

/// <summary>
/// What Catalog lets Steward propose, read through the port Assist sees: the
/// subject is the product's code, and "now" is exactly what the command would
/// change, in the culture it would change it.
/// </summary>
[Collection(PublishMetricsCollection.Name)]
public sealed class CatalogProposalsTests
{
    private static readonly TestClock Clock = new();

    [Fact]
    public async Task A_translation_is_about_the_products_code_and_snapshots_that_cultures_text()
    {
        var product = AProduct();
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var check = await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot"}"""), TestContext.Current.CancellationToken);

        Assert.True(check.IsValid);
        Assert.Equal(product.Code, check.Subject);
        Assert.Equal("""{"name":null,"description":null}""", check.Now);
    }

    [Fact]
    public async Task A_translation_into_an_unsupported_culture_is_refused()
    {
        var product = AProduct();
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var check = await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"fr","name":"Cafetière"}"""), TestContext.Current.CancellationToken);

        Assert.False(check.IsValid);
    }

    [Fact]
    public async Task Applying_a_translation_writes_it()
    {
        var product = AProduct();
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var application = await entry.ApplyAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot"}"""), TestContext.Current.CancellationToken);

        Assert.True(application.Applied);
        Assert.Equal("Moka pot", product.Name.Values["en"]);
    }

    [Fact]
    public async Task A_withdrawal_snapshots_the_status()
    {
        var product = AProduct();
        product.Publish(Clock);
        var entry = Entries(product).Single(e => e.Name == "WithdrawProduct");

        var check = await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal("""{"status":"Active"}""", check.Now);
    }

    [Fact]
    public void Every_entry_has_a_label_in_both_languages()
    {
        foreach (var entry in Entries(AProduct()))
            Assert.Equal(["en", "es"], entry.Label.Cultures.Order());
    }

    private static Product AProduct() => Product.Create(Clock, LocalizedText.From("es", "Cafetera"), new Money(10m, "EUR"));

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<IProposableCommand> Entries(Product product)
    {
        var services = new ServiceCollection();
        services.AddTenderoCqrs(typeof(SetProductTranslationCommand).Assembly);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IProductRepository>(new InMemoryProductRepository(product));
        services.AddSingleton<IUnitOfWork>(new CountingUnitOfWork());
        services.AddScoped<IProposableCommand, SetProductTranslationProposal>();
        services.AddScoped<IProposableCommand, WithdrawProductProposal>();
        return [.. services.BuildServiceProvider().CreateScope().ServiceProvider.GetServices<IProposableCommand>()];
    }
}
