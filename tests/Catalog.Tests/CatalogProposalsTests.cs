using System.Text.Json;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.SetImageAltText;
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

    /// <summary>An archived product is out of the catalogue: nothing is proposed
    /// for it, and one archived after the proposal makes it stale rather than a
    /// failure the shopkeeper did not cause (final review, M-3).</summary>
    [Fact]
    public async Task An_archived_product_is_no_subject_for_a_translation()
    {
        var product = AProduct();
        product.Archive(Clock);
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");
        var payload = Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot"}""");

        Assert.False((await entry.CheckAsync(payload, TestContext.Current.CancellationToken)).IsValid);
        Assert.Null(await entry.NowAsync(payload, TestContext.Current.CancellationToken));
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

    /// <summary>What a person reads in the card, the review and the list, written
    /// by the context that knows the product: its name, the change in words, and
    /// each field now and proposed (S-4).</summary>
    [Fact]
    public async Task A_translation_shows_the_products_name_and_the_field_it_fills()
    {
        var product = AProduct();
        var entry = Entries(product).Single(e => e.Name == "SetProductTranslation");

        var display = (await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot","description":"For the stove."}"""),
            TestContext.Current.CancellationToken)).Display!;

        Assert.Equal("Cafetera", display.Title.In("es"));
        Assert.Equal(("English name", "Nombre en inglés"), (display.Kind.In("en"), display.Kind.In("es")));
        Assert.Equal(2, display.Changes.Count);
        Assert.Equal(("Name · EN", null, "Moka pot"), (display.Changes[0].Field.In("en"), display.Changes[0].Now, display.Changes[0].Proposed?.In("es")));
        Assert.Equal(("Descripción · EN", "For the stove."), (display.Changes[1].Field.In("es"), display.Changes[1].Proposed?.In("es")));
    }

    [Fact]
    public async Task A_withdrawal_shows_the_product_going_back_to_draft()
    {
        var product = AProduct();
        product.Publish(Clock);
        var entry = Entries(product).Single(e => e.Name == "WithdrawProduct");

        var display = (await entry.CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}"}"""), TestContext.Current.CancellationToken)).Display!;

        Assert.Equal(("Withdrawal", "Retirada"), (display.Kind.In("en"), display.Kind.In("es")));
        var change = Assert.Single(display.Changes);
        // In the shopkeeper's words, both languages: never the enum's name (review of S-4, I-2).
        Assert.Equal(("Status", "On sale", "Draft"), (change.Field.In("en"), change.Now?.In("en"), change.Proposed?.In("en")));
        Assert.Equal(("Estado", "A la venta", "Borrador"), (change.Field.In("es"), change.Now?.In("es"), change.Proposed?.In("es")));
    }

    [Fact]
    public void Every_entry_has_a_label_in_both_languages()
    {
        foreach (var entry in Entries(AProduct()))
            Assert.Equal(["en", "es"], entry.Label.Cultures.Order());
    }

    // ---------- a picture's alt text ----------

    private static readonly ImageId Cover = new("9f2c");

    private static Product WithACover()
    {
        var product = AProduct();
        product.AddImage(Clock, Cover);
        return product;
    }

    private static JsonElement AltText(Product product, string image = "9f2c") => Payload(
        $$"""{"productId":"{{product.Id.Value}}","imageId":"{{image}}","spanish":"Cafetera italiana de aluminio.","english":"Aluminium moka pot."}""");

    /// <summary>
    /// The snapshot is that one picture's words, so a proposal goes stale when
    /// somebody described that picture meanwhile and not when another one changed;
    /// and the display carries which picture, for whoever reviews it to see.
    /// </summary>
    [Fact]
    public async Task Alt_text_is_about_the_products_code_snapshots_that_pictures_words_and_carries_the_picture()
    {
        var product = WithACover();
        var entry = Entries(product).Single(e => e.Name == "SetImageAltText");

        var check = await entry.CheckAsync(AltText(product), TestContext.Current.CancellationToken);

        Assert.True(check.IsValid);
        Assert.Equal(product.Code, check.Subject);
        Assert.Equal("""{"image":"9f2c","spanish":null,"english":null}""", check.Now);
        var display = check.Display!;
        Assert.Equal(("9f2c", "Cafetera", "Alt text", "Texto alternativo"),
            (display.Image, display.Title.In("es"), display.Kind.In("en"), display.Kind.In("es")));
        Assert.Equal(
            [("Alt text · ES", "Cafetera italiana de aluminio."), ("Alt text · EN", "Aluminium moka pot.")],
            display.Changes.Select(change => (change.Field.In("en"), change.Proposed!.In("en"))));
        Assert.All(display.Changes, change => Assert.Null(change.Now));
        // The two sentences are what a person may correct before approving: the payload's own fields.
        Assert.Equal(["spanish", "english"], display.Changes.Select(change => change.Key));
    }

    /// <summary>A translation's words can be corrected; taking a product off sale has no words to correct.</summary>
    [Fact]
    public async Task A_translations_words_are_a_persons_to_correct_and_a_withdrawal_has_none()
    {
        var product = AProduct();
        product.Publish(Clock);
        var entries = Entries(product);

        var translation = (await entries.Single(e => e.Name == "SetProductTranslation").CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}","culture":"en","name":"Moka pot","description":"For the stove."}"""),
            TestContext.Current.CancellationToken)).Display!;
        var withdrawal = (await entries.Single(e => e.Name == "WithdrawProduct").CheckAsync(
            Payload($$"""{"productId":"{{product.Id.Value}}"}"""), TestContext.Current.CancellationToken)).Display!;

        Assert.Equal(["name", "description"], translation.Changes.Select(change => change.Key));
        Assert.All(withdrawal.Changes, change => Assert.Null(change.Key));
    }

    [Fact]
    public async Task Alt_text_already_there_is_what_the_snapshot_and_the_display_say_is_there_now()
    {
        var product = WithACover();
        product.DescribeImage(Clock, Cover, LocalizedText.Both("A moka pot.", "Una cafetera."));
        var entry = Entries(product).Single(e => e.Name == "SetImageAltText");

        var check = await entry.CheckAsync(AltText(product), TestContext.Current.CancellationToken);

        Assert.Equal("""{"image":"9f2c","spanish":"Una cafetera.","english":"A moka pot."}""", check.Now);
        Assert.Equal(["Una cafetera.", "A moka pot."], check.Display!.Changes.Select(change => change.Now!.In("en")));
    }

    /// <summary>A picture that is not the product's, or a product out of the catalogue, is no subject: nothing is proposed, and an open proposal goes stale.</summary>
    [Fact]
    public async Task A_picture_the_product_does_not_have_and_an_archived_product_are_no_subject()
    {
        var product = WithACover();
        var entry = Entries(product).Single(e => e.Name == "SetImageAltText");

        Assert.False((await entry.CheckAsync(AltText(product, "another"), TestContext.Current.CancellationToken)).IsValid);
        Assert.Null(await entry.NowAsync(AltText(product, "another"), TestContext.Current.CancellationToken));

        product.Archive(Clock);
        Assert.Null(await entry.NowAsync(AltText(product), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Applying_alt_text_writes_both_languages()
    {
        var product = WithACover();
        var entry = Entries(product).Single(e => e.Name == "SetImageAltText");

        var application = await entry.ApplyAsync(AltText(product), TestContext.Current.CancellationToken);

        Assert.True(application.Applied);
        var alt = product.Images.Single().Alt!;
        Assert.Equal(("Cafetera italiana de aluminio.", "Aluminium moka pot."), (alt.Values["es"], alt.Values["en"]));
    }

    /// <summary>
    /// Describing a picture takes seeing it. Steward's turn carries no picture, so
    /// this one command is proposed by the job that reads pictures and by nobody
    /// who cannot (the author, 2026-10-06); the others are anybody's.
    /// </summary>
    [Fact]
    public void Only_alt_text_is_kept_for_a_job_that_sees_the_picture()
    {
        var onlyFromAJob = Entries(AProduct()).Where(entry => entry.OnlyFromAJob).Select(entry => entry.Name);

        Assert.Equal(["SetImageAltText"], onlyFromAJob);
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
        services.AddScoped<IProposableCommand, SetImageAltTextProposal>();
        return [.. services.BuildServiceProvider().CreateScope().ServiceProvider.GetServices<IProposableCommand>()];
    }
}
