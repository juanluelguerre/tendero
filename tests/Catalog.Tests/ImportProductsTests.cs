using System.Runtime.CompilerServices;
using System.Text;
using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Features.ImportProducts;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElGuerre.Tendero.Catalog.Tests;

/// <summary>
/// Importing the same product twice, with a different gallery the second time.
///
/// The seed gained an illustration for each of its six original products on
/// 2026-09-07, and the storefront kept showing the placeholders: a re-import
/// appended the new image behind the old one, and the cover is the lowest sort
/// order. Nothing was red because nothing had ever imported a product twice
/// with a different gallery — this slice had no test of its own at all.
/// </summary>
public sealed class ImportProductsTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Placeholder = new("file:///seed/placeholder.png");
    private static readonly Uri Illustration = new("file:///seed/illustration.webp");
    private static readonly Uri Back = new("file:///seed/back.webp");
    private static readonly Uri Missing = new("file:///seed/missing.webp");

    [Fact]
    public async Task Reimporting_puts_the_sources_first_image_on_the_cover()
    {
        var shop = new Shop();

        await shop.ImportAsync(Placeholder);
        await shop.ImportAsync(Illustration, Placeholder);

        Assert.Equal(Shop.IdOf(Illustration), shop.Product.PrimaryImage?.Id);
    }

    [Fact]
    public async Task An_image_the_source_dropped_leaves_the_product()
    {
        var shop = new Shop();

        await shop.ImportAsync(Placeholder);
        await shop.ImportAsync(Illustration);

        Assert.Equal(Shop.IdOf(Illustration), Assert.Single(shop.Product.Images).Id);
    }

    /// <summary>
    /// An image the source names and that cannot be read must not cost the
    /// product the gallery it already has — a CDN that times out once would
    /// otherwise strip the photos off a live product. The gallery is replaced
    /// only when every image the source lists arrived; until then the import
    /// only adds, and the next complete one converges.
    /// </summary>
    [Fact]
    public async Task An_image_that_could_not_be_read_does_not_cost_the_product_its_gallery()
    {
        var shop = new Shop();

        await shop.ImportAsync(Placeholder);
        await shop.ImportAsync(Missing, Illustration);

        Assert.Equal(Shop.IdOf(Placeholder), shop.Product.PrimaryImage?.Id);
        Assert.Contains(shop.Product.Images, image => image.Id == Shop.IdOf(Illustration));
    }

    // ---------- what a picture shows (phase 13a, I-4d; docs/analysis/catalogue-model.md) ----------

    private static readonly ExternalVariant[] ThreeColours =
    [
        new("OLIVE", new Dictionary<string, string> { ["color"] = "oliva" }),
        new("BLACK", new Dictionary<string, string> { ["color"] = "negro" }),
        new("GREY", new Dictionary<string, string> { ["color"] = "gris" }),
    ];

    private static Dictionary<string, string> Colour(string word) => new() { ["color"] = word };

    private static ImageId? PictureOf(Shop shop, string colour) =>
        shop.Product.PictureOf(shop.Product.Variants.Single(variant => variant.Sku.EndsWith(colour, StringComparison.Ordinal)))?.Id;

    /// <summary>
    /// The source says which colour a picture shows, in its own words, and the
    /// picture then shows what the variant of that colour holds — so it is that
    /// variant's. A picture that says nothing is every variant's, after their
    /// own; a colour with no picture of its own has those.
    /// </summary>
    [Fact]
    public async Task A_picture_shows_what_the_source_says_it_shows_and_is_that_variants()
    {
        var shop = new Shop { Variants = ThreeColours };
        shop.Shows(Placeholder, Colour("oliva"));
        shop.Shows(Illustration, Colour("negro"));

        await shop.ImportAsync(Placeholder, Illustration, Back);

        Assert.Equal(
            (Shop.IdOf(Placeholder), Shop.IdOf(Illustration), Shop.IdOf(Back)),
            (PictureOf(shop, "OLIVE"), PictureOf(shop, "BLACK"), PictureOf(shop, "GREY")));
        var black = shop.Product.Variants.Single(variant => variant.Sku.EndsWith("BLACK", StringComparison.Ordinal));
        Assert.Equal([Shop.IdOf(Illustration), Shop.IdOf(Back)], shop.Product.PicturesOf(black).Select(image => image.Id));
        Assert.Empty(shop.Product.Images.Single(image => image.Id == Shop.IdOf(Back)).Shows);
    }

    /// <summary>
    /// A picture carries what it shows, not its place in a list: with another
    /// image missing, the one that arrived is still told its colour, and the
    /// next complete import tells the other.
    /// </summary>
    [Fact]
    public async Task With_an_image_missing_the_pictures_that_arrived_still_say_what_they_show()
    {
        var shop = new Shop { Variants = ThreeColours };
        shop.Shows(Illustration, Colour("negro"));
        shop.Shows(Missing, Colour("oliva"));

        // The back arrives first and is the cover: black's picture is its own only if it was told so.
        await shop.ImportAsync(Back, Missing, Illustration);
        Assert.Equal((Shop.IdOf(Illustration), Shop.IdOf(Back)), (PictureOf(shop, "BLACK"), PictureOf(shop, "OLIVE")));

        shop.Shows(Placeholder, Colour("oliva"));
        await shop.ImportAsync(Back, Placeholder, Illustration);
        Assert.Equal((Shop.IdOf(Placeholder), Shop.IdOf(Illustration)), (PictureOf(shop, "OLIVE"), PictureOf(shop, "BLACK")));
    }

    /// <summary>
    /// A source that says a picture shows a colour nobody sells is wrong about
    /// the picture, not about the product: it is imported, with the picture, and
    /// the picture says nothing.
    /// </summary>
    [Fact]
    public async Task A_picture_said_to_show_a_colour_no_variant_holds_is_imported_and_shows_nothing()
    {
        var shop = new Shop { Variants = ThreeColours };
        shop.Shows(Placeholder, Colour("rojo"));

        await shop.ImportAsync(Placeholder);

        Assert.Empty(Assert.Single(shop.Product.Images).Shows);
        Assert.Equal(3, shop.Product.Variants.Count);
    }

    // ---------- what the import says beside its counts (phase 16, P16-5) ----------

    /// <summary>
    /// What the source could not hand over cleanly comes back with the counts, a
    /// note per thing: its code, the product it is about (or none, for the source
    /// as a whole), and the sentence in the language asked, which an operator
    /// reads instead of the logs.
    /// </summary>
    [Fact]
    public async Task What_the_source_could_not_hand_over_comes_back_in_the_language_asked()
    {
        var shop = new Shop { Culture = "en" };
        shop.Source.Notes = [new SourceNote(ImportNoteCode.CategoryNotMapped, OneProductConnector.ExternalId, "Coffee Makers")];
        shop.Source.SourceNotes = [new SourceNote(ImportNoteCode.LocaleIgnored, null, "fr")];

        await shop.ImportAsync();

        Assert.Equal(
            [(ImportNoteCode.CategoryNotMapped, OneProductConnector.ExternalId), (ImportNoteCode.LocaleIgnored, null)],
            shop.Result.Notes.Select(note => (note.Code, note.About)));
        Assert.Contains("“Coffee Makers”", shop.Result.Notes[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("The store publishes fr", shop.Result.Notes[1].Text, StringComparison.Ordinal);

        shop.Culture = "es";
        await shop.ImportAsync();
        Assert.StartsWith("La tienda publica fr", shop.Result.Notes[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_picture_that_could_not_be_read_is_said()
    {
        var shop = new Shop();

        await shop.ImportAsync(Missing, Illustration);

        var note = Assert.Single(shop.Result.Notes);
        Assert.Equal((ImportNoteCode.ImageUnreadable, OneProductConnector.ExternalId), (note.Code, note.About));
        Assert.Contains(Missing.ToString(), note.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A SKU the source renames is said, and ours stays: Inventory, the cart and
    /// every order speak it (spec, *Re-importing*, corrected).
    /// </summary>
    [Fact]
    public async Task A_sku_renamed_at_the_source_is_said_and_ours_stays()
    {
        var shop = new Shop { Variants = [new ExternalVariant("44001", new Dictionary<string, string>(), ExternalId: "44001", Sku: "MOKA-OLV")] };
        await shop.ImportAsync();

        shop.Variants = [new ExternalVariant("44001", new Dictionary<string, string>(), ExternalId: "44001", Sku: "MOKA-OLIVE")];
        await shop.ImportAsync();

        Assert.Equal("MOKA-OLV", Assert.Single(shop.Product.Variants).Sku);
        var note = Assert.Single(shop.Result.Notes);
        Assert.Equal(ImportNoteCode.SkuChangedAtSource, note.Code);
        Assert.Contains("MOKA-OLV → MOKA-OLIVE", note.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The proof the source is used and how much of a store comes across clean
    /// (spec, *Evidence of use*; ADR 0036): products counted by source and
    /// outcome, and the notes by code.
    /// </summary>
    [Fact]
    public async Task An_import_counts_its_products_by_outcome_and_its_notes_by_code()
    {
        using var recorder = new InstrumentRecorder("tendero.catalog.import.products", "tendero.catalog.import.notes");
        var shop = new Shop();
        shop.Source.Notes = [new SourceNote(ImportNoteCode.CategoryNotMapped, OneProductConnector.ExternalId, "Coffee Makers")];

        await shop.ImportAsync();
        await shop.ImportAsync();

        Assert.Equal(
            [("test", "created"), ("test", "updated")],
            recorder.Of("tendero.catalog.import.products").Select(tags => ((string)tags["catalog.source"]!, (string)tags["catalog.import.outcome"]!)));
        Assert.Equal(2, recorder.Sum("tendero.catalog.import.products"));
        Assert.All(recorder.Of("tendero.catalog.import.notes"), tags => Assert.Equal("CategoryNotMapped", tags["catalog.import.note"]));
        Assert.Equal(2, recorder.Sum("tendero.catalog.import.notes"));
    }

    /// <summary>
    /// A store's collection becomes the category its products share, so its old
    /// address can be sent to the category's page (phase 16, P16-11). Kept by
    /// the source's handle, the name in its URLs.
    /// </summary>
    [Fact]
    public async Task A_collection_is_kept_as_the_category_its_products_share()
    {
        var shop = new Shop();
        shop.Source.Category = "COOKWARE";
        shop.Source.Collections = [new ExternalCollection("menaje", "Menaje", [OneProductConnector.ExternalId])];

        await shop.ImportAsync();

        Assert.Equal([("test", "menaje", "COOKWARE")], shop.Collections.Kept);
        Assert.Empty(shop.Result.Notes);
    }

    /// <summary>
    /// A collection that is no category's page — its products share none, or
    /// none of them came in — is said, not sent somewhere close.
    /// </summary>
    [Theory]
    [InlineData(null, OneProductConnector.ExternalId)]
    [InlineData("COOKWARE", "NEVER_IMPORTED")]
    public async Task A_collection_that_is_no_category_is_said(string? category, string member)
    {
        var shop = new Shop { Culture = "en" };
        shop.Source.Category = category;
        shop.Source.Collections = [new ExternalCollection("ofertas", "Ofertas", [member])];

        await shop.ImportAsync();

        Assert.Empty(shop.Collections.Kept);
        var note = Assert.Single(shop.Result.Notes);
        Assert.Equal((ImportNoteCode.CollectionNotMapped, "ofertas"), (note.Code, note.About));
        Assert.Contains("Ofertas", note.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each import is a run, and its answer names it (P16-12): what a person
    /// publishes in one go once the notes are read. Only what the run created —
    /// a product it updated was already the shop's, and its status a person's.
    /// </summary>
    [Fact]
    public async Task An_import_is_a_run_that_names_the_products_it_created()
    {
        var shop = new Shop();

        await shop.ImportAsync();
        await shop.ImportAsync();

        Assert.Equal(2, shop.Runs.Added.Count);
        Assert.Equal([shop.Product.Id], shop.Runs.Added[0].Products);
        Assert.Empty(shop.Runs.Added[1].Products);
        Assert.Equal(shop.Runs.Added[1].Id.ToString(), shop.Result.ImportId);
    }

    /// <summary>
    /// The rehearsal (P16-13): the import run with nothing written — no product,
    /// no picture, no run, no collection — saying what would come and the notes.
    /// </summary>
    [Fact]
    public async Task A_rehearsal_says_what_would_come_and_writes_nothing()
    {
        var shop = new Shop { Rehearse = true };
        shop.Source.Category = "COOKWARE";
        shop.Source.Collections = [new ExternalCollection("menaje", "Menaje", [OneProductConnector.ExternalId]), new ExternalCollection("ofertas", "Ofertas", [])];

        await shop.ImportAsync(Illustration, Missing);

        Assert.Equal((1, 0, 0), (shop.Result.Created, shop.Result.Updated, shop.Result.Failed));
        Assert.Equal([ImportNoteCode.ImageUnreadable, ImportNoteCode.CollectionNotMapped], shop.Result.Notes.Select(note => note.Code));
        Assert.Null(shop.Product);
        Assert.Null(shop.Result.ImportId);
        Assert.Equal((0, 0), (shop.Saves, shop.PicturesStored));
        Assert.Empty(shop.Runs.Added);
        Assert.Empty(shop.Collections.Kept);
    }

    /// <summary>
    /// What the rehearsal says is what the import then does (spec, *Measures*):
    /// the same counts and the same notes, for a new product and for one the shop
    /// already has.
    /// </summary>
    [Fact]
    public async Task A_rehearsal_says_what_the_import_then_does()
    {
        var shop = new Shop();
        shop.Source.Collections = [new ExternalCollection("ofertas", "Ofertas", [])];

        foreach (var _ in new[] { "new", "known" })
        {
            shop.Rehearse = true;
            await shop.ImportAsync(Illustration, Missing);
            var said = shop.Result;
            shop.Rehearse = false;
            await shop.ImportAsync(Illustration, Missing);

            Assert.Equal((said.Created, said.Updated, said.Failed), (shop.Result.Created, shop.Result.Updated, shop.Result.Failed));
            Assert.Equal(said.Notes, shop.Result.Notes);
        }
    }

    private sealed class Shop
    {
        private readonly InMemoryProductRepository repository = new();
        private readonly ContentAddressedStore store = new();
        private readonly CountingUnitOfWork unitOfWork = new();

        /// <summary>The next import is a rehearsal.</summary>
        public bool Rehearse { get; set; }

        public int Saves => this.unitOfWork.SaveCount;

        public int PicturesStored => this.store.Saved;
        private readonly OneProductConnector connector = new();

        public KeptCollections Collections { get; } = new();

        public RecordedRuns Runs { get; } = new();

        public Product Product { get; private set; } = null!;

        public ImportProductsResult Result { get; private set; } = null!;

        /// <summary>The language the import's notes are asked in.</summary>
        public string Culture { get; set; } = "es";

        public OneProductConnector Source => this.connector;

        /// <summary>The ways the source sells it, from the next import on.</summary>
        public IReadOnlyList<ExternalVariant>? Variants
        {
            set => this.connector.Variants = value;
        }

        /// <summary>What the source says the picture at this location shows, from the next import on.</summary>
        public void Shows(Uri location, IReadOnlyDictionary<string, string> shows) => this.connector.Shows[location] = shows;

        /// <summary>The same product, from the same source, with this gallery.</summary>
        public async Task ImportAsync(params Uri[] images)
        {
            this.connector.Images = images;

            var handler = new ImportProductsHandler(
                new OneSourceRegistry(this.connector),
                this.repository,
                this.unitOfWork,
                new ReadableImages(),
                this.store,
                new NoAttributeDefinitions(),
                new KitchenCategories(),
                this.Collections,
                this.Runs,
                Clock,
                NullLogger<ImportProductsHandler>.Instance);

            Result = await handler.HandleAsync(new ImportProductsCommand(OneProductConnector.Key, Culture, Rehearse), Ct);

            Product = (await this.repository.FindByExternalReferenceAsync(
                OneProductConnector.Key, OneProductConnector.ExternalId, Ct))!;
        }

        /// <summary>What the store would call the bytes at this location: the
        /// images are content-addressed (ADR 0011), and here the content is the
        /// location's text.</summary>
        public static ImageId IdOf(Uri location) =>
            new(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(location.AbsolutePath)));
    }

    internal sealed class OneProductConnector : ICatalogSourceConnector
    {
        public const string Key = "test";
        public const string ExternalId = "B000000001";

        public IReadOnlyList<Uri> Images { get; set; } = [];

        public IReadOnlyList<ExternalVariant>? Variants { get; set; }

        public Dictionary<Uri, IReadOnlyDictionary<string, string>> Shows { get; } = [];

        public IReadOnlyList<SourceNote>? Notes { get; set; }

        public IReadOnlyList<SourceNote> SourceNotes { get; set; } = [];

        public string? Category { get; set; }

        public IReadOnlyList<ExternalCollection> Collections { get; set; } = [];

        public string Source => Key;

        public async IAsyncEnumerable<ExternalCollection> StreamCollectionsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var collection in Collections)
                yield return collection;
        }

        public async IAsyncEnumerable<ExternalProduct> StreamProductsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            yield return new ExternalProduct(
                ExternalId,
                new Dictionary<string, string> { ["es"] = "Cafetera de goteo", ["en"] = "Drip coffee maker" },
                Descriptions: null,
                Brand: "Kuisina",
                Category: Category,
                PriceAmount: 49.90m,
                PriceCurrency: "EUR",
                [.. Images.Select(location => new ExternalImage(location, Shows: Shows.GetValueOrDefault(location)))],
                new Dictionary<string, string>(),
                Variants: Variants,
                Notes: Notes);
        }
    }

    private sealed class OneSourceRegistry(ICatalogSourceConnector connector) : ICatalogSourceRegistry
    {
        public IReadOnlyCollection<string> Sources => [connector.Source];

        public ICatalogSourceConnector Get(string source) => connector;
    }

    /// <summary>Every location opens, except the one called "missing".</summary>
    private sealed class ReadableImages : IExternalImageReader
    {
        public Task<StoredImage?> OpenAsync(ExternalImage image, CancellationToken ct = default) =>
            Task.FromResult(image.Location == Missing
                ? null
                : new StoredImage(new MemoryStream(Encoding.UTF8.GetBytes(image.Location.AbsolutePath)), "image/webp"));
    }

    private sealed class ContentAddressedStore : IImageStore
    {
        public int Saved { get; private set; }

        public async Task<ImageId> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
        {
            Saved++;
            using var reader = new StreamReader(content);
            return new ImageId(Convert.ToHexStringLower(Encoding.UTF8.GetBytes(await reader.ReadToEndAsync(ct))));
        }

        public Task<StoredImage?> OpenAsync(ImageId id, CancellationToken ct = default) =>
            Task.FromResult<StoredImage?>(null);
    }

    private sealed class KitchenCategories : ICategoryReader
    {
        public Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default)
        {
            var kitchen = Category.Define(Clock, "KITCHEN", LocalizedText.From("es", "Cocina"));
            return Task.FromResult(new CategoryTree([kitchen, Category.Define(Clock, "COOKWARE", LocalizedText.From("es", "Menaje"), kitchen)]));
        }
    }

    /// <summary>The collections an import kept, as (source, handle, category).</summary>
    private sealed class KeptCollections : ISourceCollections
    {
        public List<(string Source, string Handle, string Category)> Kept { get; } = [];

        public Task KeepAsync(string source, string handle, string categoryCode, CancellationToken ct)
        {
            Kept.Add((source, handle, categoryCode));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordedRuns : IImportRunRepository
    {
        public List<ImportRun> Added { get; } = [];

        public void Add(ImportRun run) => Added.Add(run);

        public Task<ImportRun?> FindAsync(ImportRunId id, CancellationToken ct) =>
            Task.FromResult(Added.SingleOrDefault(run => run.Id == id));
    }

    private sealed class NoAttributeDefinitions : IAttributeDefinitionReader
    {
        public Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AttributeDefinitions.Empty);
    }
}
